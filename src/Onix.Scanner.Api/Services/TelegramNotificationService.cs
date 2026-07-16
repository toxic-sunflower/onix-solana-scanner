using System.Collections.Concurrent;
using System.Threading.Channels;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Onix.Scanner.Api.Auth;
using Onix.Scanner.Shared.Dtos;

namespace Onix.Scanner.Api.Services;

public sealed class TelegramNotificationService : BackgroundService
{
    private readonly ILogger<TelegramNotificationService> _logger;
    private readonly ITelegramBotClient? _bot;
    private readonly IServiceProvider _services;
    private readonly string _appUrl;
    private readonly JwtTokenService _jwt;
    private readonly TotpService _totp;

    private readonly ConcurrentDictionary<long, BotState> _states = new();

    private readonly Dictionary<(Guid UserId, Guid TokenId), DateTime> _lastSignalTime = new();

    private readonly Channel<TokenCardDto> _alertChannel =
        Channel.CreateBounded<TokenCardDto>(100);

    public TelegramNotificationService(
        IConfiguration config,
        ILogger<TelegramNotificationService> logger,
        IServiceProvider services,
        JwtTokenService jwt,
        TotpService totp)
    {
        _logger = logger;
        _services = services;
        _appUrl = config.GetValue<string>("App:Url") ?? "http://localhost:5000";
        _jwt = jwt;
        _totp = totp;

        var token = config["Telegram:BotToken"];

        if (!string.IsNullOrEmpty(token))
        {
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
        _logger.LogInformation("TelegramNotificationService starting");

        var alertTask = ProcessAlertsAsync(stoppingToken);

        if (_bot is not null)
        {
            try
            {
                _logger.LogInformation("Starting bot polling via StartReceiving");
                _bot.StartReceiving(
                    updateHandler: HandleUpdateAsync,
                    errorHandler: OnPollingError,
                    receiverOptions: new Telegram.Bot.Polling.ReceiverOptions
                    {
                        AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery],
                    },
                    cancellationToken: stoppingToken);
                _logger.LogInformation("Bot polling started");

                // Keep alive until cancellation
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bot polling failed");
            }
        }

        _logger.LogInformation("TelegramNotificationService stopping, awaiting alert task");
        await alertTask;
    }

    private Task OnPollingError(ITelegramBotClient bot, Exception exception, CancellationToken ct)
    {
        _logger.LogError(exception, "Telegram polling error");
        return Task.CompletedTask;
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        try
        {
            if (update.CallbackQuery is not null)
                await HandleCallbackQuery(update.CallbackQuery, ct);
            else if (update.Message?.Text is not null)
                await HandleMessage(update.Message, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling update {UpdateId}", update.Id);
        }
    }

    // ── Alert sending ──

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

    // ── Message handler ──

    private async Task HandleMessage(Message msg, CancellationToken ct)
    {
        var text = msg.Text;
        if (text is null) return;

        var chatId = msg.Chat.Id;
        var fromId = msg.From?.Id;
        if (fromId is null) return;

        // Check for active OTP challenge
        if (_states.TryGetValue(chatId, out var state) && state.State == BotStep.AwaitingOtp)
        {
            if (text.Equals("cancel", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("отмена", StringComparison.OrdinalIgnoreCase))
            {
                _totp.ClearChallenge(chatId);
                _states.TryRemove(chatId, out _);
                await ShowMainMenu(chatId, ct, "Cancelled.");
                return;
            }

            await HandleOtpInput(chatId, fromId.Value, text, state, ct);
            return;
        }

        if (text.StartsWith("/start"))
        {
            var parts = text.Split(' ');
            var payload = parts.Length > 1 ? parts[1] : "";
            await HandleStart(chatId, fromId.Value, msg.From!, payload, ct);
        }
        else if (text.Equals("/status", StringComparison.OrdinalIgnoreCase))
        {
            await _bot!.SendMessage(chatId: chatId, text: "🟢 Scanner is running", cancellationToken: ct);
        }
        else
        {
            using var scope = _services.CreateScope();
            var userRepo = scope.ServiceProvider.GetRequiredService<Core.Contracts.IUserRepository>();
            var user = await userRepo.GetByTelegramIdAsync(fromId.Value, ct);
            if (user is not null)
                await ShowMainMenu(chatId, ct);
            else
                await _bot!.SendMessage(chatId: chatId, text: "Unknown command. Send /start", cancellationToken: ct);
        }
    }

    // ── Callback query handler ──

    private async Task HandleCallbackQuery(CallbackQuery query, CancellationToken ct)
    {
        var chatId = query.Message?.Chat.Id;
        var fromId = query.From.Id;
        if (chatId is null) return;

        try
        {
            await _bot!.AnswerCallbackQuery(query.Id, cancellationToken: ct);
        }
        catch { }

        switch (query.Data)
        {
            case "register":
                await StartRegistration(chatId.Value, fromId, ct);
                break;
            case "confirm_registration":
                await CompleteRegistration(chatId.Value, fromId, ct);
                break;
            case "get_link":
                await PromptOtpForLink(chatId.Value, fromId, ct);
                break;
            case "cancel_otp":
                _totp.ClearChallenge(chatId.Value);
                _states.TryRemove(chatId.Value, out _);
                await ShowMainMenu(chatId.Value, ct, "Cancelled.");
                break;
            case "main_menu":
                _states.TryRemove(chatId.Value, out _);
                await ShowMainMenu(chatId.Value, ct);
                break;
        }
    }

    // ── /start handler ──

    private async Task HandleStart(long chatId, long fromId, User tgUser, string payload, CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<Core.Contracts.IUserRepository>();
        var user = await userRepo.GetByTelegramIdAsync(fromId, ct);

        if (payload.StartsWith("auth_"))
        {
            if (user is null)
            {
                user = new Shared.Models.User
                {
                    TelegramId = fromId,
                    TelegramUsername = tgUser.Username,
                    DisplayName = tgUser.FirstName,
                    LastLoginAt = DateTime.UtcNow,
                    Is2FAEnabled = false,
                };
                user = await userRepo.CreateAsync(user, ct);
                await userRepo.UpdateChatIdAsync(user.Id, chatId, ct);
            }

            if (!user.Is2FAEnabled)
            {
                await ShowRegistrationRequired(chatId, ct);
                return;
            }

            _totp.StartChallenge(chatId, user.Id, "auth");
            _states[chatId] = new BotState { State = BotStep.AwaitingOtp, UserId = user.Id, Purpose = "auth" };
            await _bot!.SendMessage(
                chatId: chatId,
                text: "Enter your 2FA code to log in.\n\nType the code or send *cancel* to abort.",
                parseMode: ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("Cancel", "cancel_otp")),
                cancellationToken: ct);
            return;
        }

        if (user is null)
        {
            var welcome = $"🧬 *ONIX Solana Scanner*\n\n" +
                          "Real-time arbitrage scanner between BingX Futures and Jupiter DEX.\n\n" +
                          "• Track spreads in real time\n" +
                          "• Get Telegram alerts\n" +
                          "• Manage your watchlist\n\n" +
                          "Tap *Register* to get started.";
            await _bot!.SendMessage(
                chatId: chatId,
                text: welcome,
                parseMode: ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("Register", "register")),
                cancellationToken: ct);
        }
        else
        {
            await ShowMainMenu(chatId, ct);
        }
    }

    // ── Registration flow ──

    private async Task StartRegistration(long chatId, long fromId, CancellationToken ct)
    {
        var secret = _totp.GenerateSecret();
        var qrUri = _totp.GenerateQrUri(secret, fromId.ToString());
        var backupCodes = _totp.GenerateBackupCodes(8);

        _states[chatId] = new BotState
        {
            State = BotStep.Registration,
            UserId = Guid.Empty,
            RegistrationSecret = secret,
            RegistrationBackupHashes = string.Join(",", backupCodes.Select(c => c.hash)),
        };

        var msg = $"*Step 1:* Set up two-factor authentication\n\n" +
                  $"Open your authenticator app and add a new account:\n" +
                  $"- Scan: `{qrUri}`\n\n" +
                  $"Or enter this secret manually: `{secret}`\n\n" +
                  $"*Backup codes (save these!):*\n" +
                  string.Join("\n", backupCodes.Select((c, i) => $"`{c.plain}`")) +
                  "\n\nEach backup code can be used once if you lose access to your authenticator.";

        await _bot!.SendMessage(
            chatId: chatId,
            text: msg,
            parseMode: ParseMode.Markdown,
            replyMarkup: new InlineKeyboardMarkup(
                InlineKeyboardButton.WithCallbackData("I've saved the codes — Register", "confirm_registration")),
            cancellationToken: ct);
    }

    private async Task CompleteRegistration(long chatId, long fromId, CancellationToken ct)
    {
        if (!_states.TryGetValue(chatId, out var state) || state.State != BotStep.Registration)
        {
            await ShowMainMenu(chatId, ct, "Registration expired. Try again.");
            return;
        }

        using var scope = _services.CreateScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<Core.Contracts.IUserRepository>();

        var user = await userRepo.GetByTelegramIdAsync(fromId, ct);
        if (user is null)
        {
            user = new Shared.Models.User
            {
                TelegramId = fromId,
                DisplayName = fromId.ToString(),
                LastLoginAt = DateTime.UtcNow,
                Is2FAEnabled = true,
                TwoFactorSecret = state.RegistrationSecret,
                TwoFactorBackupCodes = state.RegistrationBackupHashes,
            };
            user = await userRepo.CreateAsync(user, ct);
        }
        else
        {
            user.Is2FAEnabled = true;
            user.TwoFactorSecret = state.RegistrationSecret;
            user.TwoFactorBackupCodes = state.RegistrationBackupHashes;
            await userRepo.UpdateAsync(user, ct);
        }

        await userRepo.UpdateChatIdAsync(user.Id, chatId, ct);

        _states.TryRemove(chatId, out _);

        var authToken = _jwt.GenerateAccessToken(user.Id, user.TelegramId, user.Role, user.TokenVersion, out var jti);
        var (refreshToken, hash) = _jwt.GenerateRefreshToken();
        await userRepo.SaveRefreshTokenAsync(new Shared.Models.RefreshToken
        {
            UserId = user.Id,
            TokenHash = hash,
            LastJti = jti,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
        }, ct);

        var link = $"{_appUrl}?token={authToken}&refresh={refreshToken}";

        var msg = $"✅ Registration complete!\n\n" +
                  $"Your login link:\n{link}\n\n" +
                  "This link expires in 30 days. You can always get a new one from the menu.";
        await _bot!.SendMessage(chatId: chatId, text: msg, cancellationToken: ct);
        await ShowMainMenu(chatId, ct);
    }

    // ── OTP challenge for login link ──

    private async Task PromptOtpForLink(long chatId, long fromId, CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<Core.Contracts.IUserRepository>();
        var user = await userRepo.GetByTelegramIdAsync(fromId, ct);

        if (user is null)
        {
            await ShowRegistrationRequired(chatId, ct);
            return;
        }

        if (!user.Is2FAEnabled)
        {
            await StartRegistration(chatId, fromId, ct);
            return;
        }

        var result = _totp.StartChallenge(chatId, user.Id, "link");
        if (result.Blocked)
        {
            await _bot!.SendMessage(chatId: chatId,
                text: "Too many attempts. Try again in 5 minutes.",
                cancellationToken: ct);
            return;
        }

        _states[chatId] = new BotState { State = BotStep.AwaitingOtp, UserId = user.Id, Purpose = "link" };
        await _bot!.SendMessage(
            chatId: chatId,
            text: "Enter your 2FA code to get the login link.\n\nType the code or send *cancel* to abort.",
            parseMode: ParseMode.Markdown,
            replyMarkup: new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("Cancel", "cancel_otp")),
            cancellationToken: ct);
    }

    private async Task HandleOtpInput(long chatId, long fromId, string otp, BotState state, CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<Core.Contracts.IUserRepository>();
        var user = await userRepo.GetByTelegramIdAsync(fromId, ct);
        if (user is null)
        {
            await ShowMainMenu(chatId, ct, "User not found.");
            return;
        }

        var result = _totp.TryValidateOtp(chatId, otp, user.TwoFactorSecret, user.TwoFactorBackupCodes);

        if (result.Expired)
        {
            _states.TryRemove(chatId, out _);
            await _bot!.SendMessage(chatId: chatId, text: "Code expired. Request a new one.", cancellationToken: ct);
            return;
        }

        if (result.Blocked)
        {
            await _bot!.SendMessage(chatId: chatId,
                text: "Too many failed attempts. Try again in 5 minutes.", cancellationToken: ct);
            return;
        }

        if (!result.Validated)
        {
            var remaining = result.RemainingAttempts;
            await _bot!.SendMessage(chatId: chatId,
                text: $"Invalid code. {remaining} attempt(s) remaining.\n\nType *cancel* to abort.",
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
            return;
        }

        if (result.UsedBackup && result.MatchedHash is not null)
        {
            user.TwoFactorBackupCodes = _totp.RemoveUsedBackupCode(user.TwoFactorBackupCodes ?? "", result.MatchedHash);
            await userRepo.UpdateAsync(user, ct);
        }

        var authToken = _jwt.GenerateAccessToken(user.Id, user.TelegramId, user.Role, user.TokenVersion, out var jti);
        var (refreshToken, hash) = _jwt.GenerateRefreshToken();
        await userRepo.SaveRefreshTokenAsync(new Shared.Models.RefreshToken
        {
            UserId = user.Id,
            TokenHash = hash,
            LastJti = jti,
            DeviceName = "Telegram Bot",
            ExpiresAt = DateTime.UtcNow.AddDays(30),
        }, ct);

        _states.TryRemove(chatId, out _);

        var link = $"{_appUrl}?token={authToken}&refresh={refreshToken}";

        var linkMsg = result.UsedBackup
            ? $"✅ Login link:\n{link}\n\n⚠️ You used a backup code. Go to Settings on the website to set up a new authenticator."
            : $"✅ Login link:\n{link}\n\nThis link expires in 30 days.";

        await _bot!.SendMessage(chatId: chatId, text: linkMsg, cancellationToken: ct);
        await ShowMainMenu(chatId, ct);
    }

    // ── Helpers ──

    private async Task ShowMainMenu(long chatId, CancellationToken ct, string? preface = null)
    {
        if (preface is not null)
            await _bot!.SendMessage(chatId: chatId, text: preface, cancellationToken: ct);

        var keyboard = new InlineKeyboardMarkup([
            [InlineKeyboardButton.WithCallbackData("Get login link", "get_link")],
        ]);

        await _bot!.SendMessage(
            chatId: chatId,
            text: "📱 Main menu",
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task ShowRegistrationRequired(long chatId, CancellationToken ct)
    {
        await _bot!.SendMessage(
            chatId: chatId,
            text: "You need to set up two-factor authentication first.\n\nTap *Register* to begin.",
            parseMode: ParseMode.Markdown,
            replyMarkup: new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("Register", "register")),
            cancellationToken: ct);
    }

    // ── Signal sending ──

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

enum BotStep { None, Registration, AwaitingOtp }

class BotState
{
    public BotStep State { get; set; }
    public Guid UserId { get; set; }
    public string Purpose { get; set; } = "";
    public string? RegistrationSecret { get; set; }
    public string? RegistrationBackupHashes { get; set; }
}
