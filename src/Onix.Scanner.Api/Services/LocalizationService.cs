using System.Collections.Concurrent;
using System.Text.Json;

namespace Onix.Scanner.Api.Services;

public sealed class LocalizationService
{
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _locales = new();
    private readonly ConcurrentDictionary<long, string> _userLang = new();

    public LocalizationService()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "locales");
        if (!Directory.Exists(dir))
            dir = Path.Combine(Directory.GetCurrentDirectory(), "locales");

        if (!Directory.Exists(dir)) return;

        try
        {
            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                var json = File.ReadAllText(file);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict is null) continue;
                var code = dict.GetValueOrDefault("lang_code", "en");
                _locales[code] = dict;
            }
        }
        catch { }
    }

    public string Get(long chatId, string key, params (string Name, string Value)[] args)
    {
        var lang = _userLang.GetValueOrDefault(chatId, "ru");
        return Get(lang, key, args);
    }

    public string Get(string lang, string key, params (string Name, string Value)[] args)
    {
        if (!_locales.TryGetValue(lang, out var dict))
            dict = _locales.GetValueOrDefault("en");

        if (dict is null || !dict.TryGetValue(key, out var val))
            return $"{{{{ {key} }}}}";

        foreach (var (name, value) in args)
            val = val.Replace("{" + name + "}", value);

        return val;
    }

    public void SetLanguage(long chatId, string lang)
    {
        if (_locales.ContainsKey(lang))
            _userLang[chatId] = lang;
    }

    public string GetLanguage(long chatId) => _userLang.GetValueOrDefault(chatId, "ru");

    public IEnumerable<(string Code, string Label)> AvailableLanguages =>
        _locales.Select(kv => (kv.Key, kv.Value.GetValueOrDefault("language", kv.Key)));
}
