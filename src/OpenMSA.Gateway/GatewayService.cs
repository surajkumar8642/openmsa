using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenMSA.Core;
using OpenMSA.Identity;
using OpenMSA.Index;
using OpenMSA.Policy;
using OpenMSA.Storage;

namespace OpenMSA.Gateway;

public sealed class GatewayService
{
    private static readonly JsonSerializerOptions ApiJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex OpaqueId = new("^[A-Za-z0-9_-]{10,}$", RegexOptions.Compiled);
    private static readonly OperationKind[] QueryOnly = [OperationKind.Query];
    private static readonly OperationKind[] ReadOnly = [OperationKind.Read];

    private readonly JwtService _jwt;
    private readonly ISpaceResolver _resolver;
    private readonly IManifestStore _manifests;
    private readonly IPolicyStore _policies;
    private readonly IIndexAdapter _index;
    private readonly IStorageAdapter _storage;
    private readonly IPolicyEvaluator _policyEvaluator;
    private readonly IAuditSink _audit;
    private readonly IRateLimiter _rateLimiter;

    public GatewayService(
        JwtService jwt,
        ISpaceResolver resolver,
        IManifestStore manifests,
        IPolicyStore policies,
        IIndexAdapter index,
        IStorageAdapter storage,
        IPolicyEvaluator policyEvaluator,
        IAuditSink audit,
        IRateLimiter rateLimiter)
    {
        _jwt = jwt;
        _resolver = resolver;
        _manifests = manifests;
        _policies = policies;
        _index = index;
        _storage = storage;
        _policyEvaluator = policyEvaluator;
        _audit = audit;
        _rateLimiter = rateLimiter;
    }

    public async Task<GatewayResult<ManagedSpaceManifest>> GetManifestAsync(string spaceRef, CancellationToken cancellationToken = default)
    {
        var manifest = await ResolveManifestAsync(spaceRef, cancellationToken);
        return manifest is null
            ? GenericResponses.ForbiddenOrNotFound<ManagedSpaceManifest>()
            : GenericResponses.Ok(manifest);
    }

    public async Task<GatewayResult<string>> CreateSpaceAsync(string displayName, string jwt, CancellationToken cancellationToken = default)
    {
        var subject = ValidateToken(jwt, cancellationToken);
        if (subject is null) return GenericResponses.ForbiddenOrNotFound<string>();
        if (string.IsNullOrWhiteSpace(displayName)) return GenericResponses.Invalid<string>("name is required");

        var spaceId = IdGenerator.NewId(IdSchemes.Space);
        var manifest = new ManagedSpaceManifest(
            "openmsa.io/v1alpha1",
            "ManagedSpace",
            new ManagedSpaceMetadata(spaceId, subject.Id, displayName, "1.0.0"),
            new ManagedSpaceSpec("1.0.0", new Dictionary<string, SectionSpec>
            {
                ["private"] = new SectionSpec("private", [OperationKind.Create, OperationKind.Query, OperationKind.Read, OperationKind.Update, OperationKind.Delete]),
                ["inbox"] = new SectionSpec("inbox", [OperationKind.Deposit, OperationKind.Read, OperationKind.Query]),
                ["salesBills"] = new SectionSpec("salesBills", [OperationKind.Create, OperationKind.Deposit, OperationKind.Query, OperationKind.Read], "policies/sales-bills.json")
            }));

        if (!await _manifests.AddAsync(manifest, cancellationToken))
            return GenericResponses.Fail<string>("space creation conflict");

        await _audit.RecordAsync(new AuditEvent(DateTimeOffset.UtcNow, AuditEventType.ManifestChange, spaceId, subject.Id, "create-space", string.Empty, "allow"));
        return GenericResponses.Ok(spaceId);
    }

    public async Task<GatewayResult<ResourceEnvelope>> CreateResourceAsync(
        string spaceRef,
        string section,
        string jwt,
        string json,
        CancellationToken cancellationToken = default)
    {
        var envelope = await UpsertAsync(spaceRef, section, jwt, json, null, false, cancellationToken);
        return envelope;
    }

    public async Task<GatewayResult<ResourceEnvelope>> UpdateResourceAsync(
        string spaceRef,
        string section,
        string resourceId,
        string jwt,
        string json,
        CancellationToken cancellationToken = default)
    {
        if (!OpaqueId.IsMatch(resourceId))
            return GenericResponses.Invalid<ResourceEnvelope>("invalid resourceId");
        return await UpsertAsync(spaceRef, section, jwt, json, resourceId, true, cancellationToken);
    }

    public async Task<GatewayResult<ResourceEnvelope>> DepositInboxAsync(
        string spaceRef,
        string jwt,
        string json,
        CancellationToken cancellationToken = default)
    {
        var subject = ValidateToken(jwt, cancellationToken);
        if (subject is null) return GenericResponses.ForbiddenOrNotFound<ResourceEnvelope>();

        var manifest = await ResolveManifestAsync(spaceRef, cancellationToken);
        if (manifest is null) return GenericResponses.ForbiddenOrNotFound<ResourceEnvelope>();

        if (!await AuthorizeOperationAsync(subject, manifest, "inbox", OperationKind.Deposit))
            return GenericResponses.ForbiddenOrNotFound<ResourceEnvelope>();

        InboxDepositRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<InboxDepositRequest>(json, ApiJsonOptions);
        }
        catch
        {
            return GenericResponses.Invalid<ResourceEnvelope>("invalid payload");
        }
        if (request is null || string.IsNullOrWhiteSpace(request.StorageObjectRef))
            return GenericResponses.Invalid<ResourceEnvelope>("storageObjectRef required");
        if (string.IsNullOrWhiteSpace(request.ResourceId) || !OpaqueId.IsMatch(request.ResourceId))
            return GenericResponses.Invalid<ResourceEnvelope>("invalid resourceId");

        var existing = await _index.GetAsync(manifest.Metadata.SpaceId, request.ResourceId, cancellationToken);
        if (existing is not null)
            return GenericResponses.Invalid<ResourceEnvelope>("duplicate deposit");

        var receiverHash = GetClaim(request.Claims, "receiverMobileHash");
        if (string.IsNullOrWhiteSpace(receiverHash))
            return GenericResponses.Invalid<ResourceEnvelope>("receiverMobileHash missing");

        var resourceId = request.ResourceId;
        var claims = request.Claims.ToDictionary(k => k.Key, k => k.Value);
        var envelope = new ResourceEnvelope(
            "openmsa.io/v1alpha1",
            "InboxReference",
            new ResourceMetadata(resourceId, manifest.Metadata.SpaceId, subject.Id, "1.0.0", DateTimeOffset.UtcNow),
            claims,
            new { request.StorageObjectRef },
            request.StorageObjectRef);

        await _index.UpsertAsync(ToIndexRecord(manifest.Metadata.SpaceId, "inbox", envelope, "accepted", request.StorageObjectRef), cancellationToken);
        await _audit.RecordAsync(new AuditEvent(DateTimeOffset.UtcNow, AuditEventType.ResourceDeposit, manifest.Metadata.SpaceId, subject.Id, "inbox-deposit", resourceId, "allow"));
        return GenericResponses.Ok(envelope);
    }

    public async Task<GatewayResult<PagedResourceResponse>> ListResourcesAsync(
        string spaceRef,
        string section,
        string jwt,
        string? mobileHash,
        string? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var subject = ValidateToken(jwt, cancellationToken);
        if (subject is null) return GenericResponses.ForbiddenOrNotFound<PagedResourceResponse>();
        var manifest = await ResolveManifestAsync(spaceRef, cancellationToken);
        if (manifest is null) return GenericResponses.ForbiddenOrNotFound<PagedResourceResponse>();

        if (!await AuthorizeOperationAsync(subject, manifest, section, OperationKind.Query))
            return GenericResponses.Ok(new PagedResourceResponse(Array.Empty<ResourceSummary>()));

        if (section == "inbox" && subject.Id != manifest.Metadata.OwnerSubjectId)
            return GenericResponses.Ok(new PagedResourceResponse(Array.Empty<ResourceSummary>()));

        limit = Math.Clamp(limit, 1, 50);
        var records = await _index.QueryAsync(new IndexQuery(manifest.Metadata.SpaceId, section, mobileHash, null, null, limit, cursor), cancellationToken);

        var allowed = new List<ResourceSummary>();
        foreach (var r in records)
        {
            if (r.ResourceType == "InboxReference" && !subject.Id.Equals(manifest.Metadata.OwnerSubjectId, StringComparison.Ordinal))
                continue;

            if (section == "private" && subject.Id != manifest.Metadata.OwnerSubjectId)
                continue;

            if (!await EvaluatePolicyAsync(manifest, section, subject, r, ReadOnly))
                continue;

            allowed.Add(ToSummary(r));
        }

        await _audit.RecordAsync(new AuditEvent(DateTimeOffset.UtcNow, AuditEventType.AuthorizedRead, manifest.Metadata.SpaceId, subject.Id, "query", string.Empty, "allow"));
        var nextCursor = allowed.Count == limit ? allowed[^1].ResourceId : null;
        return GenericResponses.Ok(new PagedResourceResponse(allowed, nextCursor));
    }

    public async Task<GatewayResult<ResourceEnvelope>> GetResourceAsync(
        string spaceRef,
        string section,
        string resourceId,
        string jwt,
        CancellationToken cancellationToken = default)
    {
        var subject = ValidateToken(jwt, cancellationToken);
        if (subject is null || !OpaqueId.IsMatch(resourceId))
            return GenericResponses.ForbiddenOrNotFound<ResourceEnvelope>();
        var manifest = await ResolveManifestAsync(spaceRef, cancellationToken);
        if (manifest is null) return GenericResponses.ForbiddenOrNotFound<ResourceEnvelope>();
        if (!await AuthorizeOperationAsync(subject, manifest, section, OperationKind.Read))
            return GenericResponses.ForbiddenOrNotFound<ResourceEnvelope>();

        var indexRecord = await _index.GetAsync(manifest.Metadata.SpaceId, resourceId, cancellationToken);
        if (indexRecord is null) return GenericResponses.ForbiddenOrNotFound<ResourceEnvelope>();
        if (section == "inbox" && subject.Id != manifest.Metadata.OwnerSubjectId)
            return GenericResponses.ForbiddenOrNotFound<ResourceEnvelope>();
        if (section == "private" && subject.Id != manifest.Metadata.OwnerSubjectId)
            return GenericResponses.ForbiddenOrNotFound<ResourceEnvelope>();
        if (!await EvaluatePolicyAsync(manifest, section, subject, indexRecord, ReadOnly))
            return GenericResponses.ForbiddenOrNotFound<ResourceEnvelope>();

        var content = await _storage.GetAsync(indexRecord.StorageObjectRef, cancellationToken);
        var envelope = JsonSerializer.Deserialize<ResourceEnvelope>(Encoding.UTF8.GetString(content), ApiJsonOptions);
        if (envelope is null) return GenericResponses.Invalid<ResourceEnvelope>("invalid stored object");
        await _audit.RecordAsync(new AuditEvent(DateTimeOffset.UtcNow, AuditEventType.AuthorizedRead, manifest.Metadata.SpaceId, subject.Id, "read", resourceId, "allow"));
        return GenericResponses.Ok(envelope);
    }

    public async Task<GatewayResult<bool>> DeleteResourceAsync(
        string spaceRef,
        string section,
        string resourceId,
        string jwt,
        CancellationToken cancellationToken = default)
    {
        var subject = ValidateToken(jwt, cancellationToken);
        if (subject is null || !OpaqueId.IsMatch(resourceId))
            return GenericResponses.ForbiddenOrNotFound<bool>();
        var manifest = await ResolveManifestAsync(spaceRef, cancellationToken);
        if (manifest is null) return GenericResponses.ForbiddenOrNotFound<bool>();
        if (!await AuthorizeOperationAsync(subject, manifest, section, OperationKind.Delete))
            return GenericResponses.ForbiddenOrNotFound<bool>();

        var indexRecord = await _index.GetAsync(manifest.Metadata.SpaceId, resourceId, cancellationToken);
        if (indexRecord is null) return GenericResponses.ForbiddenOrNotFound<bool>();
        if (!await EvaluatePolicyAsync(manifest, section, subject, indexRecord, [OperationKind.Delete]))
            return GenericResponses.ForbiddenOrNotFound<bool>();

        await _storage.DeleteAsync(indexRecord.StorageObjectRef, cancellationToken);
        await _audit.RecordAsync(new AuditEvent(DateTimeOffset.UtcNow, AuditEventType.AuthorizedRead, manifest.Metadata.SpaceId, subject.Id, "delete", resourceId, "allow"));
        return GenericResponses.Ok(true);
    }

    private async Task<GatewayResult<ResourceEnvelope>> UpsertAsync(
        string spaceRef,
        string section,
        string jwt,
        string json,
        string? resourceId,
        bool replace,
        CancellationToken cancellationToken)
    {
        var subject = ValidateToken(jwt, cancellationToken);
        if (subject is null) return GenericResponses.ForbiddenOrNotFound<ResourceEnvelope>();
        var manifest = await ResolveManifestAsync(spaceRef, cancellationToken);
        if (manifest is null) return GenericResponses.ForbiddenOrNotFound<ResourceEnvelope>();

        var op = replace ? OperationKind.Update : OperationKind.Create;
        if (!await AuthorizeOperationAsync(subject, manifest, section, op))
            return GenericResponses.ForbiddenOrNotFound<ResourceEnvelope>();

        ResourceEnvelope? provided;
        try
        {
            provided = JsonSerializer.Deserialize<ResourceEnvelope>(json, ApiJsonOptions);
        }
        catch
        {
            return GenericResponses.Invalid<ResourceEnvelope>("invalid payload");
        }
        if (provided is null) return GenericResponses.Invalid<ResourceEnvelope>("invalid payload");

        if (replace && !string.Equals(provided.Metadata.ResourceId, resourceId, StringComparison.Ordinal))
            return GenericResponses.Invalid<ResourceEnvelope>("resourceId mismatch");

        var finalResourceId = resourceId
            ?? (string.IsNullOrWhiteSpace(provided.Metadata.ResourceId) || string.Equals(provided.Metadata.ResourceId, default)
                ? IdGenerator.NewId(IdSchemes.Resource)
                : provided.Metadata.ResourceId);

        provided = provided with
        {
            Metadata = new ResourceMetadata(
                finalResourceId,
                manifest.Metadata.SpaceId,
                subject.Id,
                "1.0.0",
                DateTimeOffset.UtcNow),
        };

        if (!ResourceClaimsAllowed(provided.Claims))
            return GenericResponses.Invalid<ResourceEnvelope>("invalid trusted metadata");

        if (!await EvaluatePolicyAsync(manifest, section, subject, new IndexRecord(
                provided.Metadata.ResourceId,
                manifest.Metadata.SpaceId,
                section,
                provided.Kind,
                GetClaim(provided.Claims, "receiverSubjectId"),
                GetClaim(provided.Claims, "receiverMobileHash"),
                GetClaim(provided.Claims, "billNumber"),
                DateTimeOffset.UtcNow,
                "accepted",
                string.Empty), QueryOnly))
            return GenericResponses.ForbiddenOrNotFound<ResourceEnvelope>();

        var canonical = IdGenerator.NewId("obj");
        var persisted = provided with { StorageObjectRef = canonical };
        var stored = await _storage.PutAsync(canonical, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(persisted)), "application/json", cancellationToken);
        var indexRecord = ToIndexRecord(manifest.Metadata.SpaceId, section, persisted, "accepted", stored.ObjectId);
        await _index.UpsertAsync(indexRecord, cancellationToken);
        await _audit.RecordAsync(new AuditEvent(DateTimeOffset.UtcNow, AuditEventType.AuthorizedRead, manifest.Metadata.SpaceId, subject.Id, op.ToString(), provided.Metadata.ResourceId, "allow"));
        return GenericResponses.Ok(persisted);
    }

    private bool ResourceClaimsAllowed(IReadOnlyDictionary<string, string>? claims)
    {
        if (claims is null) return false;
        if (claims.TryGetValue("receiverSubjectId", out var receiverSubjectId) && receiverSubjectId.Length > 128) return false;
        if (claims.TryGetValue("receiverMobileHash", out var receiverMobileHash) && !Regex.IsMatch(receiverMobileHash, "^[a-fA-F0-9]{64}$"))
            return false;
        if (claims.TryGetValue("billNumber", out var billNumber) && billNumber.Length > 64) return false;
        return true;
    }

    private SubjectClaims? ValidateToken(string token, CancellationToken cancellationToken)
    {
        try
        {
            return _jwt.ValidateToken(token, _jwt.Audience, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private async Task<ManagedSpaceManifest?> ResolveManifestAsync(string spaceRef, CancellationToken cancellationToken)
    {
        var spaceId = await _resolver.ResolveAsync(spaceRef, cancellationToken);
        if (spaceId is null) return null;
        return await _manifests.GetByIdAsync(spaceId, cancellationToken);
    }

    private async Task<bool> AuthorizeOperationAsync(SubjectClaims subject, ManagedSpaceManifest manifest, string section, OperationKind operation)
    {
        if (!await _rateLimiter.IsAllowedAsync(manifest.Metadata.SpaceId, $"{section}:{operation.ToString().ToLowerInvariant()}", CancellationToken.None))
            return false;

        if (!manifest.Spec.Sections.TryGetValue(section, out var sectionDef))
            return false;
        if (!sectionDef.Operations.Contains(operation))
            return false;
        if (operation == OperationKind.Delete && section != "private" && subject.Id != manifest.Metadata.OwnerSubjectId)
            return false;
        return true;
    }

    private async Task<bool> EvaluatePolicyAsync(ManagedSpaceManifest manifest, string section, SubjectClaims subject, IndexRecord indexRecord, OperationKind[] operations)
    {
        if (subject.Id == manifest.Metadata.OwnerSubjectId) return true;
        if (!manifest.Spec.Sections.TryGetValue(section, out var sectionDef))
            return false;

        var policyRef = sectionDef.PolicyRef ?? string.Empty;
        var globalPolicy = await _policies.GetPolicyAsync("global", policyRef, CancellationToken.None);
        var localPolicy = await _policies.GetPolicyAsync(manifest.Metadata.SpaceId, policyRef, CancellationToken.None);

        var hasGlobalDecision = false;
        var anyDeny = false;
        var anyAllow = false;

        foreach (var operation in operations)
        {
            var op = operation.ToString().ToLowerInvariant();

            var globalDecision = globalPolicy is null
                ? null
                : _policyEvaluator.Evaluate(globalPolicy, subject, op, indexRecord.ResourceType,
                    new Dictionary<string, object?>
                    {
                        ["receiverSubjectId"] = indexRecord.ReceiverSubjectId,
                        ["receiverMobileHash"] = indexRecord.ReceiverMobileHash,
                        ["resourceType"] = indexRecord.ResourceType,
                        ["billNumber"] = indexRecord.BillNumber
                    }!);

            var localDecision = localPolicy is null
                ? null
                : _policyEvaluator.Evaluate(localPolicy, subject, op, indexRecord.ResourceType,
                new Dictionary<string, object?>
                {
                    ["receiverSubjectId"] = indexRecord.ReceiverSubjectId,
                    ["receiverMobileHash"] = indexRecord.ReceiverMobileHash,
                    ["resourceType"] = indexRecord.ResourceType,
                    ["billNumber"] = indexRecord.BillNumber
                }!);

            if (globalDecision is not null)
            {
                hasGlobalDecision = true;
                if (!globalDecision.IsAllowed)
                {
                    anyDeny = true;
                }
                else
                {
                    anyAllow = true;
                }
            }

            if (localDecision is not null)
            {
                if (!localDecision.IsAllowed)
                {
                    anyDeny = true;
                }
                else
                {
                    anyAllow = true;
                }
            }
        }

        return hasGlobalDecision || localPolicy is not null
            ? !anyDeny && anyAllow
            : anyAllow;
    }

    private static ResourceSummary ToSummary(IndexRecord record)
        => new ResourceSummary(
            record.ResourceId,
            record.ResourceType,
            record.Section,
            record.ResourceType,
            record.CreatedAtUtc.ToString("O"),
            new Dictionary<string, string>
            {
                ["status"] = record.Status,
                ["receiverMobileHash"] = record.ReceiverMobileHash ?? string.Empty
            },
            record.Status);

    private static IndexRecord ToIndexRecord(string spaceId, string section, ResourceEnvelope envelope, string status, string storageObjectId)
    {
        var claims = envelope.Claims ?? new Dictionary<string, string>();
        return new IndexRecord(
            envelope.Metadata.ResourceId,
            spaceId,
            section,
            envelope.Kind,
            GetClaim(claims, "receiverSubjectId"),
            GetClaim(claims, "receiverMobileHash"),
            GetClaim(claims, "billNumber"),
            envelope.Metadata.CreatedAt,
            status,
            storageObjectId);
    }

    private static string? GetClaim(IDictionary<string, string>? claims, string key)
    {
        if (claims is null) return null;
        return claims.TryGetValue(key, out var value) ? value : null;
    }

    private static string? GetClaim(IReadOnlyDictionary<string, string>? claims, string key)
        => claims is IDictionary<string, string> dict ? GetClaim(dict, key) : null;
}
