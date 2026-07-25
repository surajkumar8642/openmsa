using System.Text.Json;
using OpenMSA.Core;
using OpenMSA.Identity;
using OpenMSA.Index;
using OpenMSA.Policy;
using OpenMSA.Storage;

namespace OpenMSA.Gateway.Tests;

public class GatewayBehaviorTests
{
    [Fact]
    public async Task Generic_not_found_and_unauthorized_responses_match_shape()
    {
        var (gateway, identity, _, _, _) = BuildGateway();
        var owner = await Register(identity, "+1-555-011-0001", "owner");
        var token = await identity.AuthenticateAsync(new LoginRequest("+1-555-011-0001", "Passw0rd!"), "openmsa-gateway");

        var spaceResult = await gateway.CreateSpaceAsync("MissingVisibility", token.AccessToken);
        var spaceRef = spaceResult.Value!;
        var envelope = new ResourceEnvelope(
            "openmsa.io/v1alpha1",
            "Secret",
            new ResourceMetadata(string.Empty, spaceRef, owner.SubjectId, "1.0.0", DateTimeOffset.UtcNow),
            new Dictionary<string, string> { ["receiverMobileHash"] = owner.MobileHash },
            new { note = "secret-note" });

        var created = await gateway.CreateResourceAsync(spaceRef, "private", token.AccessToken, JsonSerializer.Serialize(envelope));
        Assert.True(created.Success);

        var missingResource = await gateway.GetResourceAsync(spaceRef, "private", IdGenerator.NewId(IdSchemes.Resource), token.AccessToken);
        var badTokenResult = await gateway.GetResourceAsync(spaceRef, "private", created.Value!.Metadata.ResourceId, "bad.token.value");
        var missingSpace = await gateway.GetResourceAsync("spc_unknown", "private", created.Value!.Metadata.ResourceId, token.AccessToken);

        Assert.Equal(GatewayErrorType.NotFoundOrForbidden, missingResource.Error);
        Assert.Equal(GatewayErrorType.NotFoundOrForbidden, badTokenResult.Error);
        Assert.Equal(GatewayErrorType.NotFoundOrForbidden, missingSpace.Error);
    }

    [Fact]
    public async Task Owner_can_create_and_read_sales_bill_resource()
    {
        var (gateway, identity, _, _, _) = BuildGateway();
        var owner = await Register(identity, "+1-555-010-1000", "owner");
        var token = await identity.AuthenticateAsync(new LoginRequest("+1-555-010-1000", "Passw0rd!"), "openmsa-gateway");

        var spaceResult = await gateway.CreateSpaceAsync("Supplier", token.AccessToken);
        Assert.True(spaceResult.Success, $"create-space failed: {spaceResult.Message}");
        var spaceRef = spaceResult.Value!;

        var envelope = new ResourceEnvelope(
            "openmsa.io/v1alpha1",
            "SalesBill",
            new ResourceMetadata(string.Empty, spaceRef, owner.SubjectId, "1.0.0", DateTimeOffset.UtcNow),
            new Dictionary<string, string>
            {
                ["receiverMobileHash"] = owner.MobileHash,
                ["receiverSubjectId"] = owner.SubjectId,
                ["billNumber"] = "INV-101"
            },
            new { amount = 100 },
            string.Empty);

        var created = await gateway.CreateResourceAsync(spaceRef, "private", token.AccessToken, JsonSerializer.Serialize(envelope));
        Assert.True(created.Success, $"create-resource failed: {created.Message}");

        var read = await gateway.GetResourceAsync(spaceRef, "private", created.Value!.Metadata.ResourceId, token.AccessToken);
        Assert.True(read.Success);
        Assert.NotNull(read.Value);
        Assert.Equal("SalesBill", read.Value!.Kind);
        Assert.Equal(owner.SubjectId, read.Value.Metadata.CreatedBy);
        Assert.Equal("1.0.0", read.Value.Metadata.SchemaVersion);
    }

    [Fact]
    public async Task Visitor_cannot_read_private_resource()
    {
        var (gateway, identity, _, _, _) = BuildGateway();
        var owner = await Register(identity, "+1-555-010-2000", "owner");
        var visitor = await Register(identity, "+1-555-010-2001", "visitor");

        var ownerToken = await identity.AuthenticateAsync(new LoginRequest("+1-555-010-2000", "Passw0rd!"), "openmsa-gateway");
        var visitorToken = await identity.AuthenticateAsync(new LoginRequest("+1-555-010-2001", "Passw0rd!"), "openmsa-gateway");

        var spaceResult = await gateway.CreateSpaceAsync("PrivateSpace", ownerToken.AccessToken);
        var spaceId = spaceResult.Value!;

        var envelope = new ResourceEnvelope(
            "openmsa.io/v1alpha1",
            "Secret",
            new ResourceMetadata(string.Empty, spaceId, owner.SubjectId, "1.0.0", DateTimeOffset.UtcNow),
            new Dictionary<string, string> { ["receiverMobileHash"] = owner.MobileHash },
            new { note = "restricted" },
            string.Empty);

        var created = await gateway.CreateResourceAsync(spaceId, "private", ownerToken.AccessToken, JsonSerializer.Serialize(envelope));
        Assert.True(created.Success);

        Assert.NotNull(created.Value);
        var byVisitor = await gateway.GetResourceAsync(spaceId, "private", created.Value!.Metadata.ResourceId, visitorToken.AccessToken);
        Assert.False(byVisitor.Success);
    }

    [Fact]
    public async Task Depositor_cannot_list_or_read_inbox()
    {
        var (gateway, identity, _, _, storage) = BuildGateway();
        var owner = await Register(identity, "+1-555-010-3000", "owner");
        var sender = await Register(identity, "+1-555-010-3001", "sender");

        var ownerToken = await identity.AuthenticateAsync(new LoginRequest("+1-555-010-3000", "Passw0rd!"), "openmsa-gateway");
        var senderToken = await identity.AuthenticateAsync(new LoginRequest("+1-555-010-3001", "Passw0rd!"), "openmsa-gateway");

        var spaceResult = await gateway.CreateSpaceAsync("InboxSample", ownerToken.AccessToken);
        Assert.True(spaceResult.Success, $"create-space failed: {spaceResult.Message}");
        Assert.True(spaceResult.Success, $"create-space failed: {spaceResult.Message}");
        var spaceRef = spaceResult.Value!;

        var objectPayload = new ResourceEnvelope(
            "openmsa.io/v1alpha1",
            "SalesBill",
            new ResourceMetadata(IdGenerator.NewId(IdSchemes.Resource), spaceRef, sender.SubjectId, "1.0.0", DateTimeOffset.UtcNow),
            new Dictionary<string, string>
            {
                ["receiverMobileHash"] = owner.MobileHash,
                ["receiverSubjectId"] = owner.SubjectId,
                ["billNumber"] = "INV-999"
            },
            new { amount = 999 });

        var stored = await storage.PutAsync(
            IdGenerator.NewId("obj"),
            JsonSerializer.SerializeToUtf8Bytes(objectPayload),
            "application/json");

        var deposit = await gateway.DepositInboxAsync(spaceRef, senderToken.AccessToken,
            JsonSerializer.Serialize(new
            {
                ResourceId = IdGenerator.NewId(IdSchemes.Resource),
                StorageObjectRef = stored.ObjectId,
                Claims = new Dictionary<string, string>
                {
                    ["receiverMobileHash"] = owner.MobileHash,
                    ["receiverSubjectId"] = owner.SubjectId
                }
            }));
        Assert.True(deposit.Success, $"inbox deposit failed: {deposit.Error} {deposit.Message}");

        var senderList = await gateway.ListResourcesAsync(spaceRef, "inbox", senderToken.AccessToken, null, null, 10);
        Assert.True(senderList.Success);
        Assert.NotNull(senderList.Value);
        Assert.Empty(senderList.Value!.Items);

        var senderRead = await gateway.GetResourceAsync(spaceRef, "inbox", deposit.Value!.Metadata.ResourceId, senderToken.AccessToken);
        Assert.False(senderRead.Success);

        var ownerRead = await gateway.GetResourceAsync(spaceRef, "inbox", deposit.Value!.Metadata.ResourceId, ownerToken.AccessToken);
        Assert.True(ownerRead.Success, $"{ownerRead.Error} {ownerRead.Message}");
    }

    [Fact]
    public async Task Depositor_duplicate_inbox_reference_is_rejected()
    {
        var (gateway, identity, _, _, storage) = BuildGateway();
        var owner = await Register(identity, "+1-555-010-3100", "owner");
        var sender = await Register(identity, "+1-555-010-3101", "sender");

        var ownerToken = await identity.AuthenticateAsync(new LoginRequest("+1-555-010-3100", "Passw0rd!"), "openmsa-gateway");
        var senderToken = await identity.AuthenticateAsync(new LoginRequest("+1-555-010-3101", "Passw0rd!"), "openmsa-gateway");

        var spaceResult = await gateway.CreateSpaceAsync("InboxDuplicate", ownerToken.AccessToken);
        Assert.True(spaceResult.Success, $"create-space failed: {spaceResult.Message}");
        var spaceRef = spaceResult.Value!;

        var objectPayload = new ResourceEnvelope(
            "openmsa.io/v1alpha1",
            "SalesBill",
            new ResourceMetadata(IdGenerator.NewId(IdSchemes.Resource), spaceRef, sender.SubjectId, "1.0.0", DateTimeOffset.UtcNow),
            new Dictionary<string, string>
            {
                ["receiverMobileHash"] = owner.MobileHash,
                ["receiverSubjectId"] = owner.SubjectId,
                ["billNumber"] = "INV-DUP"
            },
            new { amount = 500 });

        var stored = await storage.PutAsync(
            IdGenerator.NewId("obj"),
            JsonSerializer.SerializeToUtf8Bytes(objectPayload),
            "application/json");

        var sharedResourceId = IdGenerator.NewId(IdSchemes.Resource);
        var first = await gateway.DepositInboxAsync(spaceRef, senderToken.AccessToken,
            JsonSerializer.Serialize(new
            {
                ResourceId = sharedResourceId,
                StorageObjectRef = stored.ObjectId,
                Claims = new Dictionary<string, string> { ["receiverMobileHash"] = owner.MobileHash }
            }));
        Assert.True(first.Success, $"first deposit failed: {first.Error} {first.Message}");

        var duplicate = await gateway.DepositInboxAsync(spaceRef, senderToken.AccessToken,
            JsonSerializer.Serialize(new
            {
                ResourceId = sharedResourceId,
                StorageObjectRef = stored.ObjectId,
                Claims = new Dictionary<string, string> { ["receiverMobileHash"] = owner.MobileHash }
            }));
        Assert.False(duplicate.Success);
        Assert.Equal(GatewayErrorType.InvalidInput, duplicate.Error);
    }

    [Fact]
    public async Task Receiver_can_read_matching_mobile_hash_and_query_by_index()
    {
        var (gateway, identity, manifestStore, policyStore, _) = BuildGateway();
        var owner = await Register(identity, "+1-555-010-4000", "owner");
        var receiver = await Register(identity, "+1-555-010-4001", "receiver");
        var outsider = await Register(identity, "+1-555-010-4002", "outsider");

        var ownerToken = await identity.AuthenticateAsync(new LoginRequest("+1-555-010-4000", "Passw0rd!"), "openmsa-gateway");
        var receiverToken = await identity.AuthenticateAsync(new LoginRequest("+1-555-010-4001", "Passw0rd!"), "openmsa-gateway");
        var outsiderToken = await identity.AuthenticateAsync(new LoginRequest("+1-555-010-4002", "Passw0rd!"), "openmsa-gateway");

        var spaceResult = await gateway.CreateSpaceAsync("SalesFlow", ownerToken.AccessToken);
        Assert.True(spaceResult.Success, $"create-space failed: {spaceResult.Message}");
        var spaceRef = spaceResult.Value!;
        Assert.NotNull(spaceRef);

        var manifest = await manifestStore.GetByIdAsync(spaceRef);
        Assert.NotNull(manifest);
        Assert.NotNull(policyStore);

        policyStore.Add(spaceRef, "policies/sales-bills.json",
            new PolicyDocument(
                "1.0",
                "deny",
                [
                    new PolicyRule(
                        "receiver-can-read",
                        "allow",
                        ["read", "query"],
                        new Condition(
                            new Dictionary<string, Dictionary<string, string>>
                            {
                                ["subject.mobile_verified"] = new() { ["equals"] = "true" },
                                ["subject.mobile_hash"] = new() { ["equalsResource"] = "resource.receiverMobileHash" }
                            },
                            null,
                            null))
                ]));

        var first = new ResourceEnvelope(
            "openmsa.io/v1alpha1",
            "SalesBill",
            new ResourceMetadata(string.Empty, spaceRef, owner.SubjectId, "1.0.0", DateTimeOffset.UtcNow),
            new Dictionary<string, string>
            {
                ["receiverMobileHash"] = receiver.MobileHash,
                ["receiverSubjectId"] = receiver.SubjectId,
                ["billNumber"] = "R-101"
            },
            new { amount = 100 },
            string.Empty);

        var second = new ResourceEnvelope(
            "openmsa.io/v1alpha1",
            "SalesBill",
            new ResourceMetadata(string.Empty, spaceRef, owner.SubjectId, "1.0.0", DateTimeOffset.UtcNow),
            new Dictionary<string, string>
            {
                ["receiverMobileHash"] = outsider.MobileHash,
                ["receiverSubjectId"] = outsider.SubjectId,
                ["billNumber"] = "R-102"
            },
            new { amount = 200 },
            string.Empty);

        var createdFirst = await gateway.CreateResourceAsync(spaceRef, "salesBills", ownerToken.AccessToken, JsonSerializer.Serialize(first));
        var createdSecond = await gateway.CreateResourceAsync(spaceRef, "salesBills", ownerToken.AccessToken, JsonSerializer.Serialize(second));
        Assert.True(createdFirst.Success);
        Assert.True(createdSecond.Success);

        var match = await gateway.ListResourcesAsync(spaceRef, "salesBills", receiverToken.AccessToken, receiver.MobileHash, null, 10);
        Assert.True(match.Success);
        Assert.NotNull(match.Value);
        Assert.Single(match.Value.Items);
        Assert.Equal(createdFirst.Value!.Metadata.ResourceId, match.Value.Items[0].ResourceId);

        var noMatch = await gateway.ListResourcesAsync(spaceRef, "salesBills", outsiderToken.AccessToken, receiver.MobileHash, null, 10);
        Assert.True(noMatch.Success);
        Assert.NotNull(noMatch.Value);
        Assert.Empty(noMatch.Value.Items);

        var readMatch = await gateway.GetResourceAsync(spaceRef, "salesBills", createdFirst.Value.Metadata.ResourceId, receiverToken.AccessToken);
        Assert.True(readMatch.Success);
        Assert.NotNull(readMatch.Value);
        Assert.Equal("SalesBill", readMatch.Value.Kind);
        Assert.False(match.Value.Items[0].Claims.ContainsKey("amount"));

        var readMismatch = await gateway.GetResourceAsync(spaceRef, "salesBills", createdSecond.Value!.Metadata.ResourceId, receiverToken.AccessToken);
        Assert.False(readMismatch.Success);
    }

    [Fact]
    public async Task Local_owner_rules_override_global_restrictions()
    {
        var (gateway, identity, _, policyStore, _) = BuildGateway();
        var owner = await Register(identity, "+1-555-010-5000", "owner");
        var other = await Register(identity, "+1-555-010-5001", "other");

        var ownerToken = await identity.AuthenticateAsync(new LoginRequest("+1-555-010-5000", "Passw0rd!"), "openmsa-gateway");
        var otherToken = await identity.AuthenticateAsync(new LoginRequest("+1-555-010-5001", "Passw0rd!"), "openmsa-gateway");

        var spaceResult = await gateway.CreateSpaceAsync("PolicyOverride", ownerToken.AccessToken);
        Assert.True(spaceResult.Success);
        var spaceRef = spaceResult.Value!;

        policyStore.Add(spaceRef, "policies/sales-bills.json",
            new PolicyDocument(
                "1.0",
                "deny",
                [new PolicyRule("deny-sales", "deny", ["read", "query"], null)]));

        var resource = new ResourceEnvelope(
            "openmsa.io/v1alpha1",
            "SalesBill",
            new ResourceMetadata(string.Empty, spaceRef, owner.SubjectId, "1.0.0", DateTimeOffset.UtcNow),
            new Dictionary<string, string>
            {
                ["receiverMobileHash"] = other.MobileHash,
                ["receiverSubjectId"] = owner.SubjectId,
                ["billNumber"] = "R-OWN"
            },
            new { amount = 3 },
            string.Empty);

        var created = await gateway.CreateResourceAsync(spaceRef, "salesBills", ownerToken.AccessToken, JsonSerializer.Serialize(resource));
        Assert.True(created.Success);

        var ownerList = await gateway.ListResourcesAsync(spaceRef, "salesBills", ownerToken.AccessToken, null, null, 10);
        Assert.True(ownerList.Success);
        Assert.NotNull(ownerList.Value);
        Assert.Single(ownerList.Value.Items);

        var ownerRead = await gateway.GetResourceAsync(spaceRef, "salesBills", created.Value!.Metadata.ResourceId, ownerToken.AccessToken);
        Assert.True(ownerRead.Success);

        var otherRead = await gateway.GetResourceAsync(spaceRef, "salesBills", created.Value.Metadata.ResourceId, otherToken.AccessToken);
        Assert.False(otherRead.Success);
    }

    [Fact]
    public async Task Invalid_resource_schema_is_rejected()
    {
        var (gateway, identity, _, _, _) = BuildGateway();
        var owner = await Register(identity, "+1-555-010-6000", "owner");
        var token = await identity.AuthenticateAsync(new LoginRequest("+1-555-010-6000", "Passw0rd!"), "openmsa-gateway");

        var spaceResult = await gateway.CreateSpaceAsync("SchemaGuard", token.AccessToken);
        Assert.True(spaceResult.Success);
        var spaceRef = spaceResult.Value!;

        var invalid = new ResourceEnvelope(
            "openmsa.io/v1alpha1",
            "SalesBill",
            new ResourceMetadata(string.Empty, spaceRef, owner.SubjectId, "1.0.0", DateTimeOffset.UtcNow),
            new Dictionary<string, string> { ["receiverMobileHash"] = "not-a-hex" },
            new { amount = 10 });

        var result = await gateway.CreateResourceAsync(spaceRef, "private", token.AccessToken, JsonSerializer.Serialize(invalid));
        Assert.False(result.Success);
        Assert.Equal(GatewayErrorType.InvalidInput, result.Error);
    }

    [Fact]
    public async Task Unauthorized_metadata_is_overwritten_by_gateway()
    {
        var (gateway, identity, _, _, _) = BuildGateway();
        var owner = await Register(identity, "+1-555-010-7000", "owner");
        var token = await identity.AuthenticateAsync(new LoginRequest("+1-555-010-7000", "Passw0rd!"), "openmsa-gateway");

        var spaceResult = await gateway.CreateSpaceAsync("MetadataOverride", token.AccessToken);
        Assert.True(spaceResult.Success);
        var spaceRef = spaceResult.Value!;

        var forged = new ResourceEnvelope(
            "openmsa.io/v1alpha1",
            "Secret",
            new ResourceMetadata(
                "forged_resource_id",
                "spc_other",
                "usr_attacker",
                "9.9.9",
                new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            new Dictionary<string, string> { ["receiverMobileHash"] = owner.MobileHash },
            new { note = "private-note" });

        var create = await gateway.CreateResourceAsync(spaceRef, "private", token.AccessToken, JsonSerializer.Serialize(forged));
        Assert.True(create.Success, $"create failed: {create.Message}");
        var stored = create.Value!;

        Assert.Equal(owner.SubjectId, stored.Metadata.CreatedBy);
        Assert.Equal(spaceRef, stored.Metadata.SpaceId);
        Assert.Equal("1.0.0", stored.Metadata.SchemaVersion);
        Assert.Equal("forged_resource_id", stored.Metadata.ResourceId);
        Assert.Equal(DateTimeOffset.UtcNow.Date, stored.Metadata.CreatedAt.Date);

        var read = await gateway.GetResourceAsync(spaceRef, "private", stored.Metadata.ResourceId, token.AccessToken);
        Assert.True(read.Success);
        Assert.NotNull(read.Value);
        Assert.Equal(owner.SubjectId, read.Value.Metadata.CreatedBy);
        Assert.Equal(spaceRef, read.Value.Metadata.SpaceId);
        Assert.Equal("1.0.0", read.Value.Metadata.SchemaVersion);
    }

    [Fact]
    public async Task Local_storage_rejects_path_traversal_object_id()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"openmsa-store-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var storage = new LocalFileStorage(tempDir);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await storage.PutAsync("../evil", Array.Empty<byte>(), "text/plain");
            });
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    private static async Task<IdentityUser> Register(IdentityService identity, string mobile, string role)
    {
        return await identity.RegisterAsync(new RegisterUserRequest(mobile, "Passw0rd!", [role]));
    }

    private static (GatewayService Gateway, IdentityService Identity, InMemoryManifestStore ManifestStore, InMemoryPolicyStore PolicyStore, LocalFileStorage Storage) BuildGateway()
    {
        var keyRing = new IdentityKeyRing();
        keyRing.GenerateNew("server");
        var jwt = new JwtService(keyRing, new JwtOptions
        {
            Issuer = "openmsa-identity",
            Audience = "openmsa-gateway",
            MobileHashSecret = "test-secret",
            Expiry = TimeSpan.FromMinutes(30)
        });

        var identity = new IdentityService(new InMemoryIdentityStore(), new PasswordService(), jwt, "test-secret");
        var manifestStore = new InMemoryManifestStore();
        var policyStore = new InMemoryPolicyStore();

        var indexPath = Path.Combine(Path.GetTempPath(), $"openmsa-index-{Guid.NewGuid()}.sqlite");
        var index = new SqliteIndexAdapter(indexPath);

        var storagePath = Path.Combine(Path.GetTempPath(), $"openmsa-store-{Guid.NewGuid()}");
        Directory.CreateDirectory(storagePath);
        var storage = new LocalFileStorage(storagePath);

        var gateway = new GatewayService(
            jwt,
            new InMemorySpaceResolver(manifestStore),
            manifestStore,
            policyStore,
            index,
            storage,
            new DeclarativePolicyEvaluator(),
            new NoopAuditSink(),
            new FixedWindowRateLimiter());

        return (gateway, identity, manifestStore, policyStore, storage);
    }
}
