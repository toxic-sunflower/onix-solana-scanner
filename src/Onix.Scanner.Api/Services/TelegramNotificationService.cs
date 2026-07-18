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
    private readonly LocalizationService _loc;

    private readonly ConcurrentDictionary<long, BotState> _states = new();
    private readonly ConcurrentDictionary<long, int> _lastScreenMsg = new();

    private readonly Dictionary<(Guid UserId, Guid TokenId), DateTime> _lastSignalTime = new();

    private readonly Channel<TokenCardDto> _alertChannel =
        Channel.CreateBounded<TokenCardDto>(100);

    public TelegramNotificationService(
        IConfiguration config,
        ILogger<TelegramNotificationService> logger,
        IServiceProvider services,
        JwtTokenService jwt,
        TotpService totp,
        LocalizationService loc)
    {
        _logger = logger;
        _services = services;
        _appUrl = config.GetValue<string>("App:Url") ?? "http://localhost:5000";
        _jwt = jwt;
        _totp = totp;
        _loc = loc;

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

        if (text.StartsWith("/start"))
        {
            try { await _bot!.DeleteMessage(chatId, msg.MessageId, ct); } catch { }
            _totp.ClearChallenge(chatId);
            _states.TryRemove(chatId, out _);
        }
        else if (_states.TryGetValue(chatId, out var state) && state.State == BotStep.AwaitingOtp)
        {
            _logger.LogInformation("HandleMessage AwaitingOtp: purpose={Purpose}, text={Text}", state.Purpose, text);
            try { await _bot!.DeleteMessage(chatId, msg.MessageId, ct); } catch { }

            if (text.Equals("cancel", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("отмена", StringComparison.OrdinalIgnoreCase))
            {
                _totp.ClearChallenge(chatId);
                _states.TryRemove(chatId, out _);
                await HandleStart(chatId, fromId.Value, msg.From!, "", 0, ct);
                return;
            }

            await HandleOtpInput(chatId, fromId.Value, text, state, ct);
            return;
        }

        if (text.StartsWith("/start"))
        {
            var parts = text.Split(' ');
            var payload = parts.Length > 1 ? parts[1] : "";
            DetectInitialLanguage(chatId, msg.From?.LanguageCode);
            await HandleStart(chatId, fromId.Value, msg.From!, payload, msg.MessageId, ct);
        }
        else if (text.Equals("/status", StringComparison.OrdinalIgnoreCase))
        {
            await _bot!.SendMessage(chatId: chatId, text: _loc.Get(chatId, "bot_running"), cancellationToken: ct);
        }
        else
        {
            using var scope = _services.CreateScope();
            var userRepo = scope.ServiceProvider.GetRequiredService<Core.Contracts.IUserRepository>();
            var user = await userRepo.GetByTelegramIdAsync(fromId.Value, ct);
            if (user is not null)
                await ShowMainMenu(chatId, ct);
            else
                await _bot!.SendMessage(chatId: chatId, text: _loc.Get(chatId, "unknown_command"), cancellationToken: ct);
        }
    }

    // ── Callback query handler ──

    private async Task HandleCallbackQuery(CallbackQuery query, CancellationToken ct)
    {
        var chatId = query.Message?.Chat.Id;
        var fromId = query.From.Id;
        if (chatId is null) return;

        DetectInitialLanguage(chatId.Value, query.From.LanguageCode);

        try
        {
            await _bot!.AnswerCallbackQuery(query.Id, cancellationToken: ct);
        }
        catch { }

        var data = query.Data;
        if (data is null) return;

        if (data.StartsWith("lang_"))
        {
            var lang = data["lang_".Length..];
            _loc.SetLanguage(chatId.Value, lang);

            using var langScope = _services.CreateScope();
            var langUserRepo = langScope.ServiceProvider.GetRequiredService<Core.Contracts.IUserRepository>();
            var langUser = await langUserRepo.GetByTelegramIdAsync(fromId, ct);
            if (langUser is not null)
            {
                langUser.Language = lang;
                await langUserRepo.UpdateAsync(langUser, ct);
            }

            var otherLangBtns = _loc.GetOtherLanguages(chatId.Value)
                .Select(l => InlineKeyboardButton.WithCallbackData(l.Label, $"lang_{l.Code}"))
                .ToArray();

            if (langUser is not null)
            {
                await _bot!.EditMessageText(
                    chatId: chatId.Value,
                    messageId: query.Message!.MessageId,
                    text: _loc.Get(lang, "main_menu"),
                    parseMode: ParseMode.Markdown,
                    replyMarkup: new InlineKeyboardMarkup([
                        [InlineKeyboardButton.WithCallbackData(_loc.Get(lang, "get_login_link"), "get_link")],
                    ]),
                    cancellationToken: ct);
            }
            else
            {
                var keyboard = otherLangBtns.Length > 0
                    ? new InlineKeyboardMarkup([
                        otherLangBtns.ToArray(),
                        [InlineKeyboardButton.WithCallbackData(_loc.Get(lang, "register_btn"), "register_0")],
                      ])
                    : new InlineKeyboardMarkup([
                        [InlineKeyboardButton.WithCallbackData(_loc.Get(lang, "register_btn"), "register_0")],
                      ]);

                await _bot!.EditMessageText(
                    chatId: chatId.Value,
                    messageId: query.Message!.MessageId,
                    text: _loc.Get(lang, "welcome"),
                    parseMode: ParseMode.Markdown,
                    replyMarkup: keyboard,
                    cancellationToken: ct);
            }
            return;
        }

        if (data.StartsWith("register_") && int.TryParse(data["register_".Length..], out var registerMsgId))
        {
            try { await _bot!.DeleteMessage(chatId.Value, query.Message!.MessageId, ct); } catch { }
            if (registerMsgId > 0)
                try { await _bot!.DeleteMessage(chatId.Value, registerMsgId, ct); } catch { }
            await StartRegistration(chatId.Value, fromId, ct);
            return;
        }

        switch (data)
        {
            case "confirm_registration":
                try { await _bot!.DeleteMessage(chatId.Value, query.Message!.MessageId, ct); } catch { }
                await CompleteRegistration(chatId.Value, fromId, ct);
                break;
            case "get_link":
                await PromptOtpForLink(chatId.Value, fromId, ct);
                break;
            case "cancel_registration":
                try { await _bot!.DeleteMessage(chatId.Value, query.Message!.MessageId, ct); } catch { }
                using (var cancelScope = _services.CreateScope())
                {
                    var cancelRepo = cancelScope.ServiceProvider.GetRequiredService<Core.Contracts.IUserRepository>();
                    var pendingUser = await cancelRepo.GetByTelegramIdAsync(fromId, ct);
                    if (pendingUser is not null && !pendingUser.Is2FAEnabled)
                        await cancelRepo.DeleteAsync(pendingUser.Id, ct);
                }
                _states.TryRemove(chatId.Value, out _);
                await HandleStart(chatId.Value, fromId, query.From!, "", 0, ct);
                break;
            case "cancel_otp":
                try { await _bot!.DeleteMessage(chatId.Value, query.Message!.MessageId, ct); } catch { }
                if (_states.TryGetValue(chatId.Value, out var otpState) && otpState.RegistrationQrMsgId > 0)
                    try { await _bot!.DeleteMessage(chatId.Value, otpState.RegistrationQrMsgId, ct); } catch { }
                _totp.ClearChallenge(chatId.Value);
                _states.TryRemove(chatId.Value, out _);
                using (var otpScope = _services.CreateScope())
                {
                    var otpRepo = otpScope.ServiceProvider.GetRequiredService<Core.Contracts.IUserRepository>();
                    var pendingUser = await otpRepo.GetByTelegramIdAsync(fromId, ct);
                    if (pendingUser is not null && !pendingUser.Is2FAEnabled)
                        await otpRepo.DeleteAsync(pendingUser.Id, ct);
                }
                await HandleStart(chatId.Value, fromId, query.From!, "", 0, ct);
                break;
            case "main_menu":
                _states.TryRemove(chatId.Value, out _);
                await ShowMainMenu(chatId.Value, ct);
                break;
        }
    }

    // ── /start handler ──

    private async Task HandleStart(long chatId, long fromId, User tgUser, string payload, int userMsgId, CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<Core.Contracts.IUserRepository>();
        var user = await userRepo.GetByTelegramIdAsync(fromId, ct);

        // Resume pending registration
        if (user is not null && !user.Is2FAEnabled && user.TwoFactorSecret is not null)
        {
            if (user.Language is not null)
                _loc.SetLanguage(chatId, user.Language);
            await ShowRegistrationBackupCodes(chatId, user, ct);
            return;
        }

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

            if (user.Language is not null)
                _loc.SetLanguage(chatId, user.Language);

            _totp.StartChallenge(chatId, user.Id, "auth");
            _states[chatId] = new BotState { State = BotStep.AwaitingOtp, UserId = user.Id, Purpose = "auth", ValidationSecret = user.TwoFactorSecret, ValidationBackupHashes = user.TwoFactorBackupCodes };
            var otpMsg = await _bot!.SendMessage(
                chatId: chatId,
                text: _loc.Get(chatId, "enter_otp"),
                parseMode: ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData(_loc.Get(chatId, "cancel"), "cancel_otp")),
                cancellationToken: ct);
            _lastScreenMsg[chatId] = otpMsg.MessageId;
            return;
        }

        if (user is null)
        {
            var currentLang = _loc.GetLanguage(chatId);
            var langBtns = _loc.GetOtherLanguages(chatId)
                .Select(l => InlineKeyboardButton.WithCallbackData(l.Label, $"lang_{l.Code}"))
                .ToArray();

            await DeletePreviousScreen(chatId, ct);

            var registerData = $"register_{userMsgId}";
            var keyboard = langBtns.Length > 0
                ? new InlineKeyboardMarkup([
                    langBtns.ToArray(),
                    [InlineKeyboardButton.WithCallbackData(_loc.Get(currentLang, "register_btn"), registerData)],
                  ])
                : new InlineKeyboardMarkup([
                    [InlineKeyboardButton.WithCallbackData(_loc.Get(currentLang, "register_btn"), registerData)],
                  ]);

            var welcomeMsg = await _bot!.SendMessage(
                chatId: chatId,
                text: _loc.Get(currentLang, "welcome"),
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard,
                cancellationToken: ct);
            _lastScreenMsg[chatId] = welcomeMsg.MessageId;
        }
        else
        {
            if (user.Language is not null)
                _loc.SetLanguage(chatId, user.Language);
            await ShowMainMenu(chatId, ct);
        }
    }

    // ── Registration flow ──

    private async Task StartRegistration(long chatId, long fromId, CancellationToken ct)
    {
        var secret = _totp.GenerateSecret();
        var qrUri = _totp.GenerateQrUri(secret, fromId.ToString());
        var backupCodes = _totp.GenerateBackupCodes(8);
        var resetCode = Guid.NewGuid().ToString("N")[..12];

        _states[chatId] = new BotState
        {
            State = BotStep.AwaitingOtp,
            UserId = Guid.Empty,
            Purpose = "register",
            ValidationSecret = secret,
            ValidationBackupHashes = string.Join(",", backupCodes.Select(c => c.hash)),
            RegistrationSecret = secret,
            RegistrationBackupHashes = string.Join(",", backupCodes.Select(c => c.hash)),
            RegistrationBackupCodes = string.Join(",", backupCodes.Select(c => c.plain)),
            RegistrationResetCode = resetCode,
        };

        _totp.StartChallenge(chatId, Guid.Empty, "register");

        await DeletePreviousScreen(chatId, ct);

        await using var ms = new MemoryStream();
        using var qrGen = new QRCoder.QRCodeGenerator();
        using var qrCode = qrGen.CreateQrCode(qrUri, QRCoder.QRCodeGenerator.ECCLevel.Q);
        using var png = new QRCoder.PngByteQRCode(qrCode);
        var pngBytes = png.GetGraphic(4);
        await ms.WriteAsync(pngBytes, ct);
        ms.Position = 0;

        var caption = $"{_loc.Get(chatId, "setup_intro")}\n\n{_loc.Get(chatId, "manual_secret", ("secret", secret))}\n\n{_loc.Get(chatId, "popular_apps")}\n<a href=\"{_appUrl}/app?name=google\">Google Authenticator</a>\n<a href=\"{_appUrl}/app?name=authy\">Authy</a>\n<a href=\"{_appUrl}/app?name=microsoft\">Microsoft Authenticator</a>\n<a href=\"{_appUrl}/app?name=2fas\">2FAS</a>";
        var qrMsg = await _bot!.SendPhoto(
            chatId: chatId,
            photo: Telegram.Bot.Types.InputFile.FromStream(ms, "qrcode.png"),
            caption: caption,
            parseMode: ParseMode.Html,
            cancellationToken: ct);

        _lastScreenMsg[chatId] = qrMsg.MessageId;
        if (_states.TryGetValue(chatId, out var s))
            s.RegistrationQrMsgId = qrMsg.MessageId;
    }

    private async Task CompleteRegistration(long chatId, long fromId, CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<Core.Contracts.IUserRepository>();

        var user = await userRepo.GetByTelegramIdAsync(fromId, ct);
        if (user is null || user.Is2FAEnabled)
        {
            await ShowMainMenu(chatId, ct);
            return;
        }

        user.Is2FAEnabled = true;
        await userRepo.UpdateAsync(user, ct);

        _states.TryRemove(chatId, out _);

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
                text: _loc.Get(chatId, "too_many_attempts"),
                cancellationToken: ct);
            return;
        }

        _states[chatId] = new BotState { State = BotStep.AwaitingOtp, UserId = user.Id, Purpose = "link", ValidationSecret = user.TwoFactorSecret, ValidationBackupHashes = user.TwoFactorBackupCodes };
        _logger.LogInformation("PromptOtpForLink: userId={UserId}, chatId={ChatId}, is2fa={Is2FA}, hasSecret={HasSecret}",
            user.Id, chatId, user.Is2FAEnabled, user.TwoFactorSecret is not null);
        await DeletePreviousScreen(chatId, ct);
        var otpMsg = await _bot!.SendMessage(
            chatId: chatId,
            text: _loc.Get(chatId, "enter_otp_for_link"),
            parseMode: ParseMode.Markdown,
            replyMarkup: new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData(_loc.Get(chatId, "cancel"), "cancel_otp")),
            cancellationToken: ct);
        _lastScreenMsg[chatId] = otpMsg.MessageId;
    }

    private async Task PromptOtpCode(long chatId, BotState state, CancellationToken ct)
    {
        var text = state.Purpose switch
        {
            "register" => _loc.Get(chatId, "enter_otp"),
            "auth" => _loc.Get(chatId, "enter_otp"),
            "link" => _loc.Get(chatId, "enter_otp_for_link"),
            _ => _loc.Get(chatId, "enter_otp"),
        };
        var promptMsg = await _bot!.SendMessage(
            chatId: chatId,
            text: text,
            parseMode: ParseMode.Markdown,
            replyMarkup: new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData(_loc.Get(chatId, "cancel"), "cancel_otp")),
            cancellationToken: ct);
        _lastScreenMsg[chatId] = promptMsg.MessageId;
    }

    private async Task HandleOtpInput(long chatId, long fromId, string otp, BotState state, CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<Core.Contracts.IUserRepository>();
        var user = await userRepo.GetByTelegramIdAsync(fromId, ct);
        if (user is null && state.Purpose != "register")
        {
            await ShowMainMenu(chatId, ct);
            return;
        }

        // Check reset code
        if (user?.TwoFactorResetCode is not null &&
            otp.Equals(user.TwoFactorResetCode, StringComparison.OrdinalIgnoreCase))
        {
            user.Is2FAEnabled = false;
            user.TwoFactorSecret = null;
            user.TwoFactorBackupCodes = null;
            user.TwoFactorResetCode = null;
            await userRepo.UpdateAsync(user, ct);

            _states.TryRemove(chatId, out _);
            await _bot!.SendMessage(chatId: chatId,
                text: _loc.Get(chatId, "2fa_reset"),
                cancellationToken: ct);
            return;
        }

        var result = _totp.TryValidateOtp(chatId, otp, state.ValidationSecret, state.ValidationBackupHashes);

        if (result.Expired)
        {
            _totp.StartChallenge(chatId, state.UserId, state.Purpose);
            await _bot!.SendMessage(chatId: chatId, text: _loc.Get(chatId, "code_expired"), cancellationToken: ct);
            await PromptOtpCode(chatId, state, ct);
            return;
        }

        if (result.Blocked)
        {
            await _bot!.SendMessage(chatId: chatId,
                text: _loc.Get(chatId, "too_many_attempts"), cancellationToken: ct);
            return;
        }

        if (!result.Validated)
        {
            var remaining = result.RemainingAttempts;
            await _bot!.SendMessage(chatId: chatId,
                text: _loc.Get(chatId, "invalid_code", ("remaining", remaining.ToString())),
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
            return;
        }

        if (state.Purpose == "register")
        {
            var lang = _loc.GetLanguage(chatId);
            user = new Shared.Models.User
            {
                TelegramId = fromId,
                DisplayName = fromId.ToString(),
                LastLoginAt = DateTime.UtcNow,
                Is2FAEnabled = false,
                TwoFactorSecret = state.RegistrationSecret,
                TwoFactorBackupCodes = state.RegistrationBackupHashes,
                TwoFactorResetCode = state.RegistrationResetCode,
                Language = lang,
            };
            user = await userRepo.CreateAsync(user, ct);
            await userRepo.UpdateChatIdAsync(user.Id, chatId, ct);

            var backupCodes = state.RegistrationBackupCodes?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];

            await DeletePreviousScreen(chatId, ct);

            var codesMsg = $"{_loc.Get(lang, "backup_codes_title")}\n" +
                           string.Join("\n", backupCodes.Select(c => $"`{c}`")) +
                           $"\n\n{_loc.Get(lang, "reset_code_label", ("code", state.RegistrationResetCode!))}\n" +
                           _loc.Get(lang, "reset_code_hint");

            var sentCodes = await _bot!.SendMessage(
                chatId: chatId,
                text: codesMsg,
                parseMode: ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup([
                    [InlineKeyboardButton.WithCallbackData(_loc.Get(lang, "register_codes_btn"), "confirm_registration")],
                    [InlineKeyboardButton.WithCallbackData(_loc.Get(lang, "cancel"), "cancel_registration")],
                ]),
                cancellationToken: ct);
            _lastScreenMsg[chatId] = sentCodes.MessageId;
            return;
        }

        if (result.UsedBackup && result.MatchedHash is not null && user is not null)
        {
            user.TwoFactorBackupCodes = _totp.RemoveUsedBackupCode(user.TwoFactorBackupCodes ?? "", result.MatchedHash);
            await userRepo.UpdateAsync(user, ct);
        }

        var authToken = _jwt.GenerateAccessToken(user!.Id, user.TelegramId, user.Role, user.TokenVersion, out var jti);
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

        var linkLang = _loc.GetLanguage(chatId);
        var link = $"{_appUrl}?token={authToken}&refresh={refreshToken}&lang={linkLang}";

        var linkKey = result.UsedBackup ? "login_link_backup" : "login_link";
        await _bot!.SendMessage(chatId: chatId,
            text: _loc.Get(chatId, linkKey, ("link", link)),
            cancellationToken: ct);
        await ShowMainMenu(chatId, ct);
    }

    // ── Language detection ──

    private void DetectInitialLanguage(long chatId, string? telegramLang)
    {
        if (telegramLang is null) return;
        if (_loc.GetLanguage(chatId) != "en") return; // already set
        var supported = _loc.AvailableLanguages.Select(l => l.Code).ToHashSet();
        if (supported.Contains(telegramLang))
            _loc.SetLanguage(chatId, telegramLang);
    }

    // ── Helpers ──

    private async Task DeletePreviousScreen(long chatId, CancellationToken ct)
    {
        if (_lastScreenMsg.TryRemove(chatId, out var msgId))
            try { await _bot!.DeleteMessage(chatId, msgId, ct); } catch { }
    }

    private async Task ShowMainMenu(long chatId, CancellationToken ct)
    {
        var keyboard = new InlineKeyboardMarkup([
            [InlineKeyboardButton.WithCallbackData(_loc.Get(chatId, "get_login_link"), "get_link")],
        ]);

        await DeletePreviousScreen(chatId, ct);
        var msg = await _bot!.SendMessage(
            chatId: chatId,
            text: _loc.Get(chatId, "main_menu"),
            replyMarkup: keyboard,
            cancellationToken: ct);
        _lastScreenMsg[chatId] = msg.MessageId;
    }

    private async Task ShowRegistrationBackupCodes(long chatId, Shared.Models.User user, CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<Core.Contracts.IUserRepository>();

        var backupCodes = _totp.GenerateBackupCodes(8);
        var resetCode = Guid.NewGuid().ToString("N")[..12];

        user.TwoFactorBackupCodes = string.Join(",", backupCodes.Select(c => c.hash));
        user.TwoFactorResetCode = resetCode;
        await userRepo.UpdateAsync(user, ct);

        var lang = _loc.GetLanguage(chatId);
        var codesMsg = $"{_loc.Get(lang, "backup_codes_title")}\n" +
                       string.Join("\n", backupCodes.Select(c => $"`{c.plain}`")) +
                       $"\n\n{_loc.Get(lang, "reset_code_label", ("code", resetCode))}\n" +
                       _loc.Get(lang, "reset_code_hint");

        var sent = await _bot!.SendMessage(
            chatId: chatId,
            text: codesMsg,
            parseMode: ParseMode.Markdown,
            replyMarkup: new InlineKeyboardMarkup([
                [InlineKeyboardButton.WithCallbackData(_loc.Get(lang, "register_codes_btn"), "confirm_registration")],
                [InlineKeyboardButton.WithCallbackData(_loc.Get(lang, "cancel"), "cancel_registration")],
            ]),
            cancellationToken: ct);
        _lastScreenMsg[chatId] = sent.MessageId;
    }

    private async Task ShowRegistrationRequired(long chatId, CancellationToken ct)
    {
        var msg = await _bot!.SendMessage(
            chatId: chatId,
            text: _loc.Get(chatId, "registration_required"),
            parseMode: ParseMode.Markdown,
            replyMarkup: new InlineKeyboardMarkup(
                InlineKeyboardButton.WithCallbackData(_loc.Get(chatId, "register_btn"), "register_0")),
            cancellationToken: ct);
        _lastScreenMsg[chatId] = msg.MessageId;
    }

    // ── Signal sending ──

    private async Task SendSignalAsync(long chatId, TokenCardDto dto, CancellationToken ct)
    {
        var spread = dto.SpreadPct.ToString("F2");
        var priceB = dto.BingxAskPrice.ToString("G");
        var priceJ = dto.JupiterBuyPrice.ToString("G");
        var id = dto.Id.ToString();
        var message = $"""
{_loc.Get(chatId, "signal_title", ("symbol", dto.Symbol), ("spread", spread))}

{_loc.Get(chatId, "signal_profit", ("spread", spread))}
{_loc.Get(chatId, "signal_bingx", ("price", priceB))}
{_loc.Get(chatId, "signal_jupiter", ("price", priceJ))}
{_loc.Get(chatId, "signal_contract", ("id", id))}
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
    public string? ValidationSecret { get; set; }
    public string? ValidationBackupHashes { get; set; }
    public string? RegistrationSecret { get; set; }
    public string? RegistrationBackupHashes { get; set; }
    public string? RegistrationBackupCodes { get; set; }
    public string? RegistrationResetCode { get; set; }
    public int RegistrationQrMsgId { get; set; }
}
