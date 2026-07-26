using System.Diagnostics.Metrics;

namespace Onix.Scanner.Core;

/// <summary>Business-level counters (TZ 19.2 "Метрики") exported via
/// OpenTelemetry's Prometheus exporter at /metrics (wired in Program.cs).
/// Kept here rather than in Api so Core stays the single place instrumented
/// code paths point to, same reasoning as SpreadCalculator.</summary>
public static class Metrics
{
    public const string MeterName = "Onix.Scanner";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly Counter<long> JupiterQuoteSuccess = Meter.CreateCounter<long>(
        "onix_jupiter_quote_success_total", description: "Successful Jupiter quote fetches");

    public static readonly Counter<long> JupiterQuoteRateLimited = Meter.CreateCounter<long>(
        "onix_jupiter_quote_ratelimited_total", description: "Jupiter quote fetches that hit a 429");

    public static readonly Counter<long> JupiterQuoteError = Meter.CreateCounter<long>(
        "onix_jupiter_quote_error_total", description: "Jupiter quote fetches that errored (network/proxy/parse failure)");

    public static readonly Counter<long> JupiterQuoteSkippedBackoff = Meter.CreateCounter<long>(
        "onix_jupiter_quote_skipped_backoff_total", description: "Jupiter quote attempts skipped due to an active per-token 429 backoff");

    public static readonly Counter<long> BingxReconnects = Meter.CreateCounter<long>(
        "onix_bingx_reconnects_total", description: "BingX WebSocket reconnects");

    public static readonly Counter<long> TelegramSignalsSent = Meter.CreateCounter<long>(
        "onix_telegram_signals_sent_total", description: "Telegram spread-alert signals sent");

    public static readonly Counter<long> SpreadTicksWritten = Meter.CreateCounter<long>(
        "onix_spread_ticks_written_total", description: "Spread ticks persisted to the database");
}
