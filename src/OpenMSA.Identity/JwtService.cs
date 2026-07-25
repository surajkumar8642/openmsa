using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using OpenMSA.Core;

namespace OpenMSA.Identity;

public sealed class JwtOptions
{
    public string Issuer { get; init; } = "openmsa-identity";
    public string Audience { get; init; } = "openmsa-gateway";
    public TimeSpan Expiry { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan NotBeforeOffset { get; init; } = TimeSpan.Zero;
    public TimeSpan ClockSkew { get; init; } = TimeSpan.FromMinutes(1);
    public string? MobileHashSecret { get; init; }
}

public sealed class JwtService
{
    private readonly IdentityKeyRing _keyRing;
    private readonly JwtOptions _options;
    private readonly JwtSecurityTokenHandler _handler = new();
    public TimeSpan Expiry => _options.Expiry;
    public string Audience => _options.Audience;

    public JwtService(IdentityKeyRing keyRing, JwtOptions options)
    {
        _keyRing = keyRing;
        _options = options;
    }

    public string IssueToken(IdentityUser user)
    {
        var now = DateTimeOffset.UtcNow;
        var mobileHash = user.MobileHash!;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.SubjectId),
            new("mobile_verified", user.MobileVerified ? "true" : "false"),
            new("mobile_hash", mobileHash),
            new("jti", IdGenerator.NewId(IdSchemes.Token))
        };
        foreach (var role in user.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var signingCredentials = new SigningCredentials(_keyRing.ActiveKey, SecurityAlgorithms.RsaSha256);

        var token = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            NotBefore = now.Add(_options.NotBeforeOffset).UtcDateTime,
            Expires = now.Add(_options.Expiry).UtcDateTime,
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            SigningCredentials = signingCredentials
        };

        var jwt = _handler.CreateToken(token);
        return _handler.WriteToken(jwt);
    }

    public SubjectClaims ValidateToken(string token, string audience, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new SecurityTokenException("Token missing");

        var validationParameters = new TokenValidationParameters
        {
            ValidIssuers = [_options.Issuer],
            ValidAudiences = [audience],
            IssuerSigningKeys = _keyRing.Keys.Cast<SecurityKey>(),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            RequireSignedTokens = true,
            RequireAudience = true,
            RequireExpirationTime = true,
            ClockSkew = _options.ClockSkew,
            ValidAlgorithms = ["RS256"]
        };

        _handler.InboundClaimTypeMap.Clear();
        var principal = _handler.ValidateToken(token, validationParameters, out _);
        var subject = principal.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value ?? string.Empty;
        var mobileVerified = bool.TryParse(principal.Claims.FirstOrDefault(c => c.Type == "mobile_verified")?.Value, out var mv) && mv;
        var mobileHash = principal.Claims.FirstOrDefault(c => c.Type == "mobile_hash")?.Value;
        var roles = new List<string>();
        foreach (var role in principal.Claims.Where(c => c.Type == ClaimTypes.Role))
            roles.Add(role.Value);

        return new SubjectClaims(subject, mobileHash, mobileVerified, roles);
    }

    public string PublicJwksJson()
    {
        var keys = new List<Dictionary<string, string>>();
        foreach (var key in _keyRing.Keys)
        {
            var p = key.Rsa ?? throw new InvalidOperationException("Invalid key.");
            var parameters = key.Rsa.ExportParameters(false);
            if (parameters.Exponent == null || parameters.Modulus == null) continue;

            keys.Add(new Dictionary<string, string>
            {
                ["kty"] = "RSA",
                ["use"] = "sig",
                ["alg"] = "RS256",
                ["kid"] = key.KeyId,
                ["n"] = Base64UrlEncoder.Encode(parameters.Modulus),
                ["e"] = Base64UrlEncoder.Encode(parameters.Exponent)
            });
        }

        var body = new Dictionary<string, object?>
        {
            ["keys"] = keys
        };
        return JsonSerializer.Serialize(body);
    }
}
