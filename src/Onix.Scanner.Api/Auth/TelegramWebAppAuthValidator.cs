using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Onix.Scanner.Api.Auth;

/// <summary>Validates Telegram Mini App `initData` (docs.telegram-mini-apps.com/
/// platform/init-data#validating-init-data) — separate from
/// <see cref="TelegramOpenIdValidator"/>, which validates the "Log In With
/// Telegram" OAuth id_token used by the plain web login. A Mini App opened
/// inside Telegram already knows who the user is via this signed payload;
/// making them go through the OAuth redirect flow on top of that is the
/// "почему миниапп требует отдельно залогиниться" complaint this exists to fix.</summary>
public sealed class TelegramWebAppAuthValidator
{
    private readonly string? _botToken;

    public TelegramWebAppAuthValidator(IConfiguration config) =>
        _botToken = config["Telegram:BotToken"];

    public bool TryValidate(string initData, out long telegramId, out string? username, out string? displayName)
    {
        telegramId = 0;
        username = null;
        displayName = null;

        if (string.IsNullOrEmpty(_botToken) || string.IsNullOrWhiteSpace(initData))
            return false;

        Dictionary<string, string> pairs;
        try
        {
            pairs = initData.Split('&')
                .Select(p => p.Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        }
        catch
        {
            return false;
        }

        if (!pairs.TryGetValue("hash", out var hash) || string.IsNullOrEmpty(hash))
            return false;

        var dataCheckString = string.Join('\n',
            pairs.Where(kv => kv.Key != "hash")
                 .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                 .Select(kv => $"{kv.Key}={kv.Value}"));

        var secretKey = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(_botToken));
        var computedHash = Convert.ToHexStringLower(HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString)));

        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(computedHash), Encoding.UTF8.GetBytes(hash)))
            return false;

        // Replay protection — a captured initData string is only meant to be
        // usable right after Telegram hands it to the Mini App, not forever.
        if (pairs.TryGetValue("auth_date", out var authDateStr)
            && long.TryParse(authDateStr, out var authDateUnix)
            && DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(authDateUnix) > TimeSpan.FromDays(1))
            return false;

        if (!pairs.TryGetValue("user", out var userJson) || string.IsNullOrEmpty(userJson))
            return false;

        using var doc = JsonDocument.Parse(userJson);
        var root = doc.RootElement;
        if (!root.TryGetProperty("id", out var idEl))
            return false;

        telegramId = idEl.GetInt64();
        username = root.TryGetProperty("username", out var u) ? u.GetString() : null;
        displayName = root.TryGetProperty("first_name", out var fn) ? fn.GetString() : username;
        return true;
    }
}
