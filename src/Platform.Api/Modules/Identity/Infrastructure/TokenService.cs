using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Platform.Modules.Identity.Domain;

namespace Platform.Modules.Identity.Infrastructure;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "fashion-platform";
    public string Audience { get; set; } = "fashion-platform-api";
    /// <summary>Min 32 bytes. Never commit it: use user-secrets locally and a secret store in deployed environments.</summary>
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
}

public interface ITokenService
{
    (string Token, DateTime ExpiresUtc) CreateAccessToken(User user, DateTime nowUtc);
    (string Raw, string Hash) CreateRefreshToken();
    string Hash(string rawRefreshToken);
}

public sealed class TokenService(IOptions<JwtOptions> options) : ITokenService
{
    private static readonly JsonWebTokenHandler Handler = new();

    // The access token deliberately carries NO tenant or role: users can belong to several tenants,
    // and tenant + role are validated per request against live membership (spec 4.2, 12.2).
    public (string Token, DateTime ExpiresUtc) CreateAccessToken(User user, DateTime nowUtc)
    {
        var o = options.Value;
        var expires = nowUtc.AddMinutes(o.AccessTokenMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.SigningKey));
        var claims = new List<Claim>
        {
            new("sub", user.UserId.ToString()),
            new("email", user.Email),
            new("jti", Guid.NewGuid().ToString())
        };
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = o.Issuer,
            Audience = o.Audience,
            NotBefore = nowUtc,
            IssuedAt = nowUtc,
            Expires = expires,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };
        return (Handler.CreateToken(descriptor), expires);
    }

    public (string Raw, string Hash) CreateRefreshToken()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (raw, Hash(raw));
    }

    public string Hash(string rawRefreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawRefreshToken))).ToLowerInvariant();
}
