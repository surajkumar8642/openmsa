using System.Security.Cryptography;
using System.Text;

namespace OpenMSA.Core;

public static class IdSchemes
{
    public const string Space = "spc";
    public const string Resource = "res";
    public const string User = "usr";
    public const string Token = "tok";
}

public static class IdGenerator
{
    public static string NewId(string prefix)
    {
        Span<byte> bytes = stackalloc byte[12];
        RandomNumberGenerator.Fill(bytes);
        var encoded = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"{prefix}_{encoded}";
    }
}

public enum OperationKind
{
    Create,
    Read,
    Update,
    Delete,
    Query,
    Deposit
}

public static class OperationKindExtensions
{
    public static bool Allows(this OperationKind op, IEnumerable<OperationKind> allowed)
        => allowed.Contains(op);
}

public enum AuditEventType
{
    AuthenticationSuccess,
    AuthenticationFailure,
    TokenVerificationFailure,
    ResourceDeposit,
    AuthorizedRead,
    DeniedRequest,
    PolicyValidationFailure,
    ManifestChange,
    KeyRotation
}

public sealed record AuditEvent(
    DateTimeOffset TimestampUtc,
    AuditEventType Type,
    string SpaceId,
    string SubjectId,
    string Operation,
    string ResourceId,
    string Decision,
    string? Details = null);

public sealed record ResourceMetadata(
    string ResourceId,
    string SpaceId,
    string CreatedBy,
    string SchemaVersion,
    DateTimeOffset CreatedAt)
{
    public ResourceMetadata()
        : this(string.Empty, string.Empty, string.Empty, "1.0.0", DateTimeOffset.UtcNow) { }
}

public sealed record ResourceEnvelope(
    string ApiVersion,
    string Kind,
    ResourceMetadata Metadata,
    IReadOnlyDictionary<string, string>? Claims,
    object? Spec,
    string? StorageObjectRef = null)
{
    public ResourceEnvelope()
        : this("openmsa.io/v1alpha1", string.Empty, new ResourceMetadata(), null, null, null) { }
}

public sealed record SubjectClaims(
    string Id,
    string? MobileHash = null,
    bool MobileVerified = false,
    IReadOnlyList<string>? Roles = null,
    IReadOnlyDictionary<string, string>? ExtraClaims = null);

public sealed record RequestContext(
    string SpaceId,
    SubjectClaims Subject,
    string Section,
    OperationKind Operation,
    string? ResourceId = null,
    string? CorrelationId = null);

public enum Decision
{
    Allow,
    Deny
}

public sealed record AuthorizationDecision(Decision Decision, string? Reason = null)
{
    public bool IsAllowed => Decision == Decision.Allow;
}

public sealed record ApiError(string Type, string Title, string Detail, int Status);

public sealed record IdentityPolicyIdentity(
    string SubjectId,
    string? MobileNormalized,
    string MobileHash,
    IReadOnlyList<string> Roles,
    bool MobileVerified);

public static class MobileHasher
{
    public static string Normalize(string mobile)
    {
        var digits = new string([.. mobile.Where(char.IsDigit)]);
        if (digits.Length == 0) return string.Empty;
        if (digits.StartsWith("0") && digits.Length > 10)
        {
            digits = digits.TrimStart('0');
        }
        return digits;
    }

    public static string HashNormalized(string normalizedMobile, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Mobile hashing key is not configured.");

        var keyBytes = Encoding.UTF8.GetBytes(key);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(normalizedMobile));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
