using System.Threading.Channels;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Onix.Scanner.Shared.Dtos;

namespace Onix.Scanner.Api.Services;

public sealed class TelegramNotificationService : BackgroundService
{
    private readonly ILogger<TelegramNotificationService> _logger;
    private readonly ITelegramBotClient? _bot;
    private readonly IServiceProvider _services;
    private readonly string? _botToken;

    private readonly Dictionary<(Guid UserId, Guid TokenId), DateTime> _lastSignalTime = new();

    private readonly Channel<TokenCardDto> _alertChannel =
        Channel.CreateBounded<TokenCardDto>(100);
    private long _lastUpdateId;

    public TelegramNotificationService(
        IConfiguration config,
        ILogger<TelegramNotificationService> logger,
        IServiceProvider services)
    {
        _logger = logger;
        _services = services;

        var token = config["Telegram:BotToken"];

        if (!string.IsNullOrEmpty(token))
        {
            _botToken = token;
            _bot = new TelegramBotClient(token);
            _logger.LogInformation("Telegram bot initialized");
        }
        else
        {
            _logger.LogWarning("Telegram bot not configured — notifications disabled");
        }
    }

    public void EnqueueAlert(TokenCardDto dto)
    {
        _alertChannel.Writer.TryWrite(dto);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_bot is null) return;

        var alertTask = ProcessAlertsAsync(stoppingToken);
        var pollingTask = PollCommandsAsync(stoppingToken);

        await Task.WhenAny(alertTask, pollingTask);
    }

    private async Task ProcessAlertsAsync(CancellationToken stoppingToken)
    {
        while (await _alertChannel.Reader.WaitToReadAsync(stoppingToken))
        {
            while (_alertChannel.Reader.TryRead(out var dto))
            {
                try
                {
                    using var scope = _services.CreateScope();
                    var userRepo = scope.ServiceProvider.GetRequiredService<Core.Contracts.IUserRepository>();
                    var subscribers = await userRepo.GetSubscribersAsync(dto.Id, stoppingToken);

                    foreach (var sub in subscribers)
                    {
                        if (dto.SpreadPct < sub.AlertThresholdPct) continue;

                        var key = (sub.UserId, dto.Id);
                        if (_lastSignalTime.TryGetValue(key, out var lastTime))
                        {
                            if (DateTime.UtcNow - lastTime < TimeSpan.FromSeconds(sub.CooldownSeconds))
                                continue;
                        }
                        _lastSignalTime[key] = DateTime.UtcNow;

                        await SendSignalAsync(sub.ChatId, dto, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send Telegram signal for {Symbol}", dto.Symbol);
                }
            }
        }
    }

    private async Task PollCommandsAsync(CancellationToken ct)
    {
        var http = new HttpClient();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var url = $"https://api.telegram.org/bot{_botToken}/getUpdates" +
                          $"?offset={_lastUpdateId}&allowed_updates=[\"message\"]";
                var response = await http.GetStringAsync(url, ct);
                using var doc = System.Text.Json.JsonDocument.Parse(response);
                var result = doc.RootElement.GetProperty("result");
                var updates = System.Text.Json.JsonSerializer.Deserialize<Update[]>(result.GetRawText());

                if (updates is null || updates.Length == 0) continue;

                foreach (var update in updates)
                {
                    _lastUpdateId = update.Id + 1;
                    if (update.Message?.Text is null) continue;

                    var text = update.Message.Text;
                    var chatId = update.Message.Chat.Id;
                    var fromId = update.Message.From?.Id;

                    if (text.StartsWith("/start"))
                    {
                        var parts = text.Split(' ');
                        var payload = parts.Length > 1 ? parts[1] : "";

                        if (payload.StartsWith("auth_") && long.TryParse(payload["auth_".Length..], out var tid) && fromId == tid)
                        {
                            using var scope = _services.CreateScope();
                            var userRepo = scope.ServiceProvider.GetRequiredService<Core.Contracts.IUserRepository>();
                            var user = await userRepo.GetByTelegramIdAsync(tid, ct);
                            if (user is null)
                            {
                                user = new Shared.Models.User
                                {
                                    TelegramId = tid,
                                    TelegramUsername = update.Message.From!.Username,
                                    DisplayName = update.Message.From.FirstName,
                                    LastLoginAt = DateTime.UtcNow
                                };
                                user = await userRepo.CreateAsync(user, ct);
                            }

                            await userRepo.UpdateChatIdAsync(user.Id, chatId, ct);

                            var authToken = Guid.NewGuid().ToString("N");
                            var expiresAt = DateTime.UtcNow.AddDays(30);
                            await userRepo.UpdateAuthTokenAsync(user.Id, authToken, expiresAt, ct);

                            await _bot.SendMessage(
                                chatId: chatId,
                                text: $"✅ Auth successful! Your token: `{authToken}`\nUse this in X-Auth-Token header.",
                                parseMode: ParseMode.Markdown,
                                cancellationToken: ct);
                        }
                        else
                        {
                            await _bot.SendMessage(
                                chatId: chatId,
                                text: "Welcome! Use /start auth_YOUR_TELEGRAM_ID to authenticate.",
                                cancellationToken: ct);
                        }
                    }
                    else if (text.Equals("/status", StringComparison.OrdinalIgnoreCase))
                    {
                        await _bot.SendMessage(
                            chatId: chatId,
                            text: "🟢 Scanner is running",
                            cancellationToken: ct);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Telegram polling error");
            }

            await Task.Delay(2000, ct);
        }
    }

    private async Task SendSignalAsync(long chatId, TokenCardDto dto, CancellationToken ct)
    {
        var message = $"""
🚨 {dto.Symbol}  {dto.SpreadPct:F2}% (Solana)

💰 Token: {dto.Symbol}
📈 Profit: {dto.SpreadPct:F2}%
💵 BINGX: ${dto.BingxAskPrice:G}
💵 Jupiter: ${dto.JupiterBuyPrice:G}
📋 Contract: {dto.Id}
""";

        var inlineKeyboard = new InlineKeyboardMarkup([
            [InlineKeyboardButton.WithUrl("BINGX", dto.BingxUrl),
             InlineKeyboardButton.WithUrl("Jupiter", dto.JupiterUrl)],
            [InlineKeyboardButton.WithUrl("Contract", dto.SolscanUrl),
             InlineKeyboardButton.WithUrl("Chart", $"/chart/{dto.Id}")],
        ]);

        var sent = await _bot!.SendMessage(
            chatId: chatId,
            text: message,
            replyMarkup: inlineKeyboard,
            cancellationToken: ct);

        _logger.LogInformation("Telegram signal sent: chat_id={ChatId} message_id={MessageId} symbol={Symbol} spread={Spread:F2}%",
            chatId, sent.MessageId, dto.Symbol, dto.SpreadPct);
    }
}
