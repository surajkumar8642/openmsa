namespace OpenMSA.Identity;

public sealed record RegisterUserRequest(string Mobile, string Password, IReadOnlyList<string> Roles);

public sealed record LoginRequest(string Mobile, string Password);

public sealed record IdentityUser(
    string SubjectId,
    string MobileNormalized,
    string PasswordHash,
    string MobileHash,
    IReadOnlyList<string> Roles,
    bool MobileVerified,
    DateTimeOffset CreatedAtUtc);

public sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAtUtc);

public sealed class IdentitySigningKey
{
    public string KeyId { get; init; } = string.Empty;
    public string PrivatePem { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
