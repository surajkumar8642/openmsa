using Microsoft.IdentityModel.Tokens;

namespace OpenMSA.Identity.Tests;

public class JwtTests
{
    [Fact]
    public async Task Register_and_authenticate_issue_and_validate_token()
    {
        var keyRing = new IdentityKeyRing();
        keyRing.GenerateNew("k1");
        var jwtService = new JwtService(keyRing, new JwtOptions
        {
            Issuer = "openmsa-identity",
            Audience = "openmsa-gateway",
            Expiry = TimeSpan.FromMinutes(30),
            MobileHashSecret = "test-secret"
        });
        var identity = new IdentityService(new InMemoryIdentityStore(), new PasswordService(), jwtService, "test-secret");

        await identity.RegisterAsync(new RegisterUserRequest("+1-555-111-2222", "Passw0rd!", ["member"]));
        var token = await identity.AuthenticateAsync(new LoginRequest("+1-555-111-2222", "Passw0rd!"), "openmsa-gateway");

        var claims = identity.ValidateToken(token.AccessToken, "openmsa-gateway");
        Assert.NotEmpty(claims.Id);
        Assert.True(claims.MobileVerified);
        Assert.Matches("^usr_", claims.Id);
        Assert.Matches("^[a-f0-9]{64}$", claims.MobileHash);
    }

    [Fact]
    public void Modified_token_is_rejected()
    {
        var keyRing = new IdentityKeyRing();
        keyRing.GenerateNew("k1");
        var jwtService = new JwtService(keyRing, new JwtOptions { MobileHashSecret = "test-secret" });
        var identity = new IdentityService(new InMemoryIdentityStore(), new PasswordService(), jwtService, "test-secret");
        var user = new IdentityUser("usr_mod", "15550102030", "hash", "abcd", ["member"], true, DateTimeOffset.UtcNow);
        keyRing.GenerateNew("k2");

        var token = jwtService.IssueToken(user);
        var bad = token[..^2] + "aa";

        Assert.ThrowsAny<SecurityTokenException>(() => identity.ValidateToken(bad, "openmsa-gateway"));
    }

    [Fact]
    public void Expired_token_is_rejected()
    {
        var keyRing = new IdentityKeyRing();
        keyRing.GenerateNew("k1");
        // Use a token whose expiry is intentionally in the past to avoid timer flake.
        var jwtService = new JwtService(keyRing, new JwtOptions
        {
            Issuer = "openmsa-identity",
            Audience = "openmsa-gateway",
            Expiry = TimeSpan.FromMinutes(-2),
            NotBeforeOffset = TimeSpan.FromMinutes(-3),
            MobileHashSecret = "test-secret"
        });
        var identity = new IdentityService(new InMemoryIdentityStore(), new PasswordService(), jwtService, "test-secret");
        var user = new IdentityUser("usr_01J_exp", "15550102030", "hash", "abcd", ["member"], true, DateTimeOffset.UtcNow);
        var token = jwtService.IssueToken(user);

        Assert.Throws<SecurityTokenExpiredException>(() => identity.ValidateToken(token, "openmsa-gateway"));
    }

    [Fact]
    public async Task Wrong_audience_is_rejected()
    {
        var keyRing = new IdentityKeyRing();
        keyRing.GenerateNew("k1");
        var jwtService = new JwtService(keyRing, new JwtOptions { MobileHashSecret = "test-secret", Audience = "openmsa-gateway" });
        var identity = new IdentityService(new InMemoryIdentityStore(), new PasswordService(), jwtService, "test-secret");
        await identity.RegisterAsync(new RegisterUserRequest("+1-555-111-1111", "Passw0rd!", ["member"]));
        var token = await identity.AuthenticateAsync(new LoginRequest("+1-555-111-1111", "Passw0rd!"), "openmsa-gateway");

        Assert.Throws<SecurityTokenInvalidAudienceException>(() => identity.ValidateToken(token.AccessToken, "different-aud"));
    }

    [Fact]
    public void Wrong_issuer_is_rejected()
    {
        var keyRing = new IdentityKeyRing();
        keyRing.GenerateNew("k1");
        var badIssuerService = new JwtService(keyRing, new JwtOptions { MobileHashSecret = "test-secret", Issuer = "bad-issuer", Audience = "openmsa-gateway" });
        var user = new IdentityUser("usr_iss", "15550102030", "hash", "abcd", ["member"], true, DateTimeOffset.UtcNow);
        var token = badIssuerService.IssueToken(user);

        var goodIssuerService = new JwtService(keyRing, new JwtOptions { MobileHashSecret = "test-secret", Issuer = "good-issuer", Audience = "openmsa-gateway" });
        var validator = new IdentityService(new InMemoryIdentityStore(), new PasswordService(), goodIssuerService, "test-secret");

        Assert.Throws<SecurityTokenInvalidIssuerException>(() => validator.ValidateToken(token, "openmsa-gateway"));
    }

    [Fact]
    public void Alg_none_is_rejected()
    {
        var keyRing = new IdentityKeyRing();
        keyRing.GenerateNew("k1");
        var jwtService = new JwtService(keyRing, new JwtOptions { MobileHashSecret = "test-secret" });
        var identity = new IdentityService(new InMemoryIdentityStore(), new PasswordService(), jwtService, "test-secret");
        var user = new IdentityUser("usr_none", "15550102030", "hash", "abcd", ["member"], true, DateTimeOffset.UtcNow);
        var token = jwtService.IssueToken(user);

        var segments = token.Split('.');
        var payload = segments[1];
        var forged = $"eyJhbGciOiJub25lIn0.{payload}.";

        Assert.ThrowsAny<SecurityTokenException>(() => identity.ValidateToken(forged, "openmsa-gateway"));
    }
}
