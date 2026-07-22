using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Onix.Scanner.Api.Auth;

/// <summary>
/// Verifies the ID token returned by Telegram's "Log In With Telegram" OAuth
/// 2.0 / OpenID Connect flow (core.telegram.org/bots/telegram-login):
/// authorization code + PKCE, code exchanged server-side for an id_token via
/// https://oauth.telegram.org/token. This class only checks the id_token's
/// signature/claims against Telegram's published JWKS — the actual code→token
/// exchange (which needs the client secret) happens in
/// <see cref="TelegramOAuthClient"/>.
///
/// Verified against two independent sources (core.telegram.org/bots/telegram-login
/// docs + cross-check search) on 2026-07-22: discovery/JWKS URLs, endpoint
/// shapes and claim names below. The Telegram user ID is the "id" claim, NOT
/// "sub" (sub is an opaque OIDC subject identifier).
/// </summary>
public sealed class TelegramOpenIdValidator
{
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configManager;
    private readonly string _clientId;

    public TelegramOpenIdValidator(IConfiguration config, HttpClient httpClient)
    {
        var discoveryUrl = config.GetValue<string>("Telegram:OpenId:DiscoveryUrl")
            ?? "https://oauth.telegram.org/.well-known/openid-configuration";
        _clientId = config.GetValue<string>("Telegram:OpenId:ClientId")
            ?? throw new InvalidOperationException("Telegram:OpenId:ClientId is required");

        _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            discoveryUrl,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(httpClient) { RequireHttps = true });
    }

    public async Task<ClaimsPrincipal?> ValidateAsync(string idToken, CancellationToken ct)
    {
        var oidcConfig = await _configManager.GetConfigurationAsync(ct);

        var handler = new JwtSecurityTokenHandler();
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = oidcConfig.Issuer,
            ValidateAudience = true,
            ValidAudience = _clientId,
            ValidateLifetime = true,
            IssuerSigningKeys = oidcConfig.SigningKeys,
            ValidateIssuerSigningKey = true,
        };

        try
        {
            var principal = handler.ValidateToken(idToken, parameters, out _);
            return principal;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
    }

    /// <summary>The real Telegram user ID (not the OIDC "sub" claim).</summary>
    public static long? GetTelegramId(ClaimsPrincipal principal)
    {
        var idClaim = principal.FindFirstValue("id");
        return long.TryParse(idClaim, out var id) ? id : null;
    }
}
