using OpenMSA.Core;

namespace OpenMSA.Identity;

public sealed class IdentityService
{
    private readonly IIdentityStore _store;
    private readonly PasswordService _passwordService;
    private readonly JwtService _jwtService;
    private readonly string _mobileHashSecret;

    public IdentityService(IIdentityStore store, PasswordService passwordService, JwtService jwtService, string mobileHashSecret)
    {
        _store = store;
        _passwordService = passwordService;
        _jwtService = jwtService;
        _mobileHashSecret = mobileHashSecret;
    }

    public async Task<IdentityUser> RegisterAsync(RegisterUserRequest request, string? role = null, CancellationToken cancellationToken = default)
    {
        var normalized = MobileHasher.Normalize(request.Mobile);
        var existing = await _store.FindByMobileAsync(normalized, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var user = new IdentityUser(
            SubjectId: IdGenerator.NewId(IdSchemes.User),
            MobileNormalized: normalized,
            PasswordHash: _passwordService.HashPassword(request.Password),
            MobileHash: MobileHasher.HashNormalized(normalized, _mobileHashSecret),
            Roles: request.Roles.Count > 0 ? request.Roles : new[] { role ?? "member" },
            MobileVerified: true,
            CreatedAtUtc: DateTimeOffset.UtcNow);

        await _store.AddUserAsync(user, cancellationToken);
        return user;
    }

    public async Task<IssuedToken> AuthenticateAsync(LoginRequest request, string audience, CancellationToken cancellationToken = default)
    {
        var normalized = MobileHasher.Normalize(request.Mobile);
        var user = await _store.FindByMobileAsync(normalized, cancellationToken);
        if (user is null) throw new UnauthorizedAccessException("Invalid credentials.");

        var ok = _passwordService.VerifyPassword(user.PasswordHash, request.Password);
        if (!ok) throw new UnauthorizedAccessException("Invalid credentials.");

        return new IssuedToken(_jwtService.IssueToken(user), DateTimeOffset.UtcNow.Add(_jwtService.Expiry));
    }

    public SubjectClaims ValidateToken(string token, string? audience = null)
        => _jwtService.ValidateToken(token, audience ?? _jwtService.Audience);

    public string PublicJwks() => _jwtService.PublicJwksJson();
}
