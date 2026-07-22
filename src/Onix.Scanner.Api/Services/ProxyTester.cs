using System.Diagnostics;
using System.Net;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Api.Services;

public static class ProxyTester
{
    private const string ProbeUrl = "https://api.jup.ag/price/v3?ids=So11111111111111111111111111111111111111112";

    public sealed record Result(bool Success, int? LatencyMs, string? Error);

    public static async Task<Result> TestAsync(Proxy proxy, CancellationToken ct)
    {
        try
        {
            using var handler = new HttpClientHandler();
            if (proxy.Type.Equals("SOCKS5", StringComparison.OrdinalIgnoreCase))
            {
                handler.Proxy = new WebProxy($"socks5://{proxy.Host}:{proxy.Port}");
            }
            else
            {
                var uri = string.IsNullOrEmpty(proxy.Username)
                    ? new Uri($"http://{proxy.Host}:{proxy.Port}")
                    : new Uri($"http://{proxy.Username}:{proxy.Password}@{proxy.Host}:{proxy.Port}");
                handler.Proxy = new WebProxy(uri);
            }

            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            var sw = Stopwatch.StartNew();
            using var response = await client.GetAsync(ProbeUrl, ct);
            sw.Stop();

            if (!response.IsSuccessStatusCode)
                return new Result(false, (int)sw.ElapsedMilliseconds, $"HTTP {(int)response.StatusCode}");

            return new Result(true, (int)sw.ElapsedMilliseconds, null);
        }
        catch (Exception ex)
        {
            return new Result(false, null, ex.Message);
        }
    }
}
