using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Onix.Scanner.Shared;

namespace Onix.Scanner.Api.Auth;

public class JwtTokenService
{
    private readonly SymmetricSecurityKey _key;

    public JwtTokenService(byte[] key)
    {
        _key = new SymmetricSecurityKey(key);
    }

    public string GenerateAccessToken(Guid userId, long telegramId, UserRole role, int tokenVersion)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("telegram_id", telegramId.ToString()),
            new Claim(ClaimTypes.Role, role.ToString()),
            new Claim("token_version", tokenVersion.ToString()),
        };

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (string refreshToken, string sha256Hash) GenerateRefreshToken()
    {
        var random = new byte[64];
        RandomNumberGenerator.Fill(random);
        var token = Convert.ToHexString(random).ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(random)).ToLowerInvariant();
        return (token, hash);
    }

    public static string HashRefreshToken(string token)
    {
        var bytes = Convert.FromHexString(token);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
