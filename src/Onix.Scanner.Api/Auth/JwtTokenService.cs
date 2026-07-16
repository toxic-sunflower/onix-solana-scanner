using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
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

    public string GenerateToken(Guid userId, long telegramId, UserRole role)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("telegram_id", telegramId.ToString()),
            new Claim(ClaimTypes.Role, role.ToString()),
        };

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddDays(30),
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
