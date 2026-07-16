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
    private readonly string _appUrl;
    private readonly Auth.JwtTokenService _jwt;

    private readonly Dictionary<(Guid UserId, Guid TokenId), DateTime> _lastSignalTime = new();

    private readonly Channel<TokenCardDto> _alertChannel =
        Channel.CreateBounded<TokenCardDto>(100);
    private long _lastUpdateId;

    public TelegramNotificationService(
        IConfiguration config,
        ILogger<TelegramNotificationService> logger,
        IServiceProvider services,
        Auth.JwtTokenService jwt)
    {
        _logger = logger;
        _services = services;
        _appUrl = config.GetValue<string>("App:Url") ?? "http://localhost:5000";
        _jwt = jwt;

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

                        if (payload.StartsWith("auth_"))
                        {
                            if (fromId is null) continue;

                            using var scope = _services.CreateScope();
                            var userRepo = scope.ServiceProvider.GetRequiredService<Core.Contracts.IUserRepository>();

                            var user = new Shared.Models.User
                            {
                                TelegramId = fromId.Value,
                                TelegramUsername = update.Message.From!.Username,
                                DisplayName = update.Message.From.FirstName,
                                LastLoginAt = DateTime.UtcNow
                            };
                            user = await userRepo.CreateAsync(user, ct);

                            await userRepo.UpdateChatIdAsync(user.Id, chatId, ct);

                            var authToken = _jwt.GenerateAccessToken(user.Id, user.TelegramId, user.Role, user.TokenVersion, out var jti);
                            var (refreshToken, hash) = _jwt.GenerateRefreshToken();
                            await userRepo.SaveRefreshTokenAsync(new Shared.Models.RefreshToken
                            {
                                UserId = user.Id,
                                TokenHash = hash,
                                LastJti = jti,
                                ExpiresAt = DateTime.UtcNow.AddDays(30),
                            }, ct);

                            await _bot.SendMessage(
                                chatId: chatId,
                                text: $"✅ Auth successful!\n\nOpen Mini App: {_appUrl}?token={authToken}&refresh={refreshToken}",
                                cancellationToken: ct);
                        }
                        else
                        {
                            await _bot.SendMessage(
                                chatId: chatId,
                                text: "Welcome to ONIX Solana Scanner!\n\nGo to http://89.124.82.95 to log in.",
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
                    else if (text.Equals("/logout", StringComparison.OrdinalIgnoreCase))
                    {
                        if (fromId is null) continue;

                        using var scope = _services.CreateScope();
                        var userRepo = scope.ServiceProvider.GetRequiredService<Core.Contracts.IUserRepository>();
                        var user = await userRepo.GetByTelegramIdAsync(fromId.Value, ct);
                        if (user is not null)
                        {
                            await userRepo.DeleteUserRefreshTokensAsync(user.Id, ct);
                            await userRepo.IncrementTokenVersionAsync(user.Id, ct);
                        }

                        await _bot.SendMessage(
                            chatId: chatId,
                            text: "🔒 Logged out of all devices",
                            cancellationToken: ct);
                    }
                    else if (text.Equals("/sessions", StringComparison.OrdinalIgnoreCase))
                    {
                        if (fromId is null) continue;

                        using var scope = _services.CreateScope();
                        var userRepo = scope.ServiceProvider.GetRequiredService<Core.Contracts.IUserRepository>();
                        var user = await userRepo.GetByTelegramIdAsync(fromId.Value, ct);
                        if (user is null)
                        {
                            await _bot.SendMessage(chatId: chatId, text: "No account found. Use /start to create one.", cancellationToken: ct);
                            continue;
                        }

                        var sessions = await userRepo.GetSessionsAsync(user.Id, ct);
                        if (sessions.Count == 0)
                        {
                            await _bot.SendMessage(chatId: chatId, text: "No active sessions.", cancellationToken: ct);
                            continue;
                        }

                        var msg = $"*{sessions.Count} active session(s):*\n\n" +
                                  string.Join("\n", sessions.Select((s, i) =>
                                      $"{i + 1}. {s.DisplayName}\n" +
                                      $"   IP: {s.IpAddress ?? "N/A"}\n" +
                                      $"   Last used: {s.LastUsedAt?.ToString("g") ?? "Never"}\n" +
                                      $"   Created: {s.CreatedAt:g}"));

                        await _bot.SendMessage(chatId: chatId, text: msg, parseMode: ParseMode.Markdown, cancellationToken: ct);
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
