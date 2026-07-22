using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Onix.Scanner.Api.Auth;

/// <summary>
/// Server-side half of Telegram's OAuth 2.0 Authorization Code + PKCE flow
/// (core.telegram.org/bots/telegram-login). Exchanges the authorization
/// "code" the frontend received for an id_token. This is the only place
/// the client secret is used — it never reaches the browser.
/// </summary>
public sealed class TelegramOAuthClient
{
    private readonly HttpClient _http;
    private readonly string _tokenEndpoint;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _redirectUri;

    public TelegramOAuthClient(IConfiguration config, HttpClient httpClient)
    {
        _http = httpClient;
        _tokenEndpoint = config.GetValue<string>("Telegram:OpenId:TokenEndpoint")
            ?? "https://oauth.telegram.org/token";
        _clientId = config.GetValue<string>("Telegram:OpenId:ClientId")
            ?? throw new InvalidOperationException("Telegram:OpenId:ClientId is required");
        _clientSecret = config.GetValue<string>("Telegram:OpenId:ClientSecret")
            ?? throw new InvalidOperationException("Telegram:OpenId:ClientSecret is required");
        _redirectUri = config.GetValue<string>("Telegram:OpenId:RedirectUri")
            ?? throw new InvalidOperationException("Telegram:OpenId:RedirectUri is required");
    }

    /// <returns>The raw id_token JWT, or null if the exchange failed.</returns>
    public async Task<string?> ExchangeCodeForIdTokenAsync(string code, string codeVerifier, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _tokenEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}")));
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _redirectUri,
            ["client_id"] = _clientId,
            ["code_verifier"] = codeVerifier,
        });

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("id_token", out var idTokenEl) ? idTokenEl.GetString() : null;
    }
}
