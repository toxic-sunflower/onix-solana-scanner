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
    private readonly string? _webhookSecret;
    private readonly JwtTokenService _jwt;
    private readonly LocalizationService _loc;

    /// <summary>Compared against the X-Telegram-Bot-Api-Secret-Token header on
    /// incoming webhook requests, so only Telegram (which we told the secret
    /// via SetWebhookAsync) can feed us updates.</summary>
    public string? WebhookSecret => _webhookSecret;

    private readonly ConcurrentDictionary<long, int> _lastScreenMsg = new();

    private readonly Channel<TokenCardDto> _alertChannel =
        Channel.CreateBounded<TokenCardDto>(100);

    public TelegramNotificationService(
        IConfiguration config,
        ILogger<TelegramNotificationService> logger,
        IServiceProvider services,
        JwtTokenService jwt,
        LocalizationService loc)
    {
        _logger = logger;
        _services = services;
        _appUrl = (config.GetValue<string>("App:Url") ?? "http://localhost:5000").Trim();
        if (!_appUrl.StartsWith("http://") && !_appUrl.StartsWith("https://"))
            _appUrl = "https://" + _appUrl;
        _jwt = jwt;
        _loc = loc;
        _webhookSecret = config["Telegram:WebhookSecret"];

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
            // Webhooks instead of long-polling: with blue/green deploys, two
            // instances briefly run side by side, and Telegram only allows
            // ONE getUpdates poller per bot token — the loser gets 409
            // Conflict, which used to take the whole process down. Setting
            // the webhook is idempotent: both instances pointing it at the
            // same URL during the overlap window is harmless, unlike polling.
            if (string.IsNullOrEmpty(_webhookSecret))
            {
                _logger.LogError("Telegram:WebhookSecret is not configured — webhook not registered, bot updates disabled");
            }
            else
            {
                try
                {
                    var webhookUrl = $"{_appUrl}/api/v1/telegram/webhook";
                    await _bot.SetWebhook(
                        url: webhookUrl,
                        secretToken: _webhookSecret,
                        allowedUpdates: [UpdateType.Message, UpdateType.CallbackQuery],
                        cancellationToken: stoppingToken);
                    _logger.LogInformation("Telegram webhook registered at {Url}", webhookUrl);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to register Telegram webhook");
                }
            }
        }

        _logger.LogInformation("TelegramNotificationService alert loop running");
        await alertTask;
    }

    /// <summary>Entry point for TelegramWebhookController — processes one
    /// Update delivered by Telegram over HTTP.</summary>
    public async Task HandleWebhookUpdateAsync(Update update, CancellationToken ct)
    {
        if (_bot is null) return;

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

    /// <summary>Called right after a user logs in via "Log In With Telegram"
    /// OAuth, so the bot dialog starts immediately instead of waiting for the
    /// user to find the bot and send /start manually. For private chats
    /// Telegram's chat_id equals the user's numeric Telegram ID, so this only
    /// works if the user has a chat with the bot open already (which the
    /// OAuth authorization flow itself requires — it happens inside
    /// Telegram, against this bot). Best-effort: if it fails (e.g. the user
    /// somehow never opened the bot chat), the manual /start path still
    /// works exactly as before.</summary>
    public async Task<bool> TryStartDialogAsync(Guid userId, long telegramId, CancellationToken ct)
    {
        if (_bot is null) return false;

        try
        {
            using var scope = _services.CreateScope();
            var userRepo = scope.ServiceProvider.GetRequiredService<Core.Contracts.IUserRepository>();
            await userRepo.UpdateChatIdAsync(userId, telegramId, ct);

            var user = await userRepo.GetByIdAsync(userId, ct);
            if (user?.Language is not null)
                _loc.SetLanguage(telegramId, user.Language);

            await SetMenuButtonAsync(telegramId, ct);
            await ShowMainMenu(telegramId, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex,
                "Could not proactively start Telegram dialog for user {UserId} — they'll get it on their next /start instead",
                userId);
            return false;
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
                        if (dto.SpreadPct < sub.AlertThresholdPct)
                        {
                            // Rearm: once spread drops back below threshold, the next
                            // crossing is allowed to signal immediately again.
                            if (!sub.IsArmed)
                                await userRepo.SetAlertStateAsync(sub.UserId, dto.Id, null, isArmed: true, stoppingToken);
                            continue;
                        }

                        var cooldownElapsed = sub.LastSignalAt is null ||
                            DateTime.UtcNow - sub.LastSignalAt.Value >= TimeSpan.FromSeconds(sub.CooldownSeconds);
                        if (!sub.IsArmed && !cooldownElapsed) continue;

                        await userRepo.SetAlertStateAsync(sub.UserId, dto.Id, DateTime.UtcNow, isArmed: false, stoppingToken);
                        await SendSignalAsync(sub.ChatId, dto, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Shutting down (e.g. container stop) — not a real failure.
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
            DetectInitialLanguage(chatId, msg.From?.LanguageCode);
            await HandleStart(chatId, fromId.Value, msg.From!, ct);
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

            await SetMenuButtonAsync(chatId.Value, ct);

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
                    cancellationToken: ct);
            }
            else
            {
                var keyboard = new InlineKeyboardMarkup([
                    ..(otherLangBtns.Length > 0 ? new[] { otherLangBtns.ToArray() } : Array.Empty<InlineKeyboardButton[]>()),
                    [InlineKeyboardButton.WithUrl(_loc.Get(lang, "open_app_btn"), _appUrl)],
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

        switch (data)
        {
            case "main_menu":
                try { await _bot!.DeleteMessage(chatId.Value, query.Message!.MessageId, ct); } catch { }
                await ShowMainMenu(chatId.Value, ct);
                break;
        }
    }

    // ── /start handler ──

    private async Task SetMenuButtonAsync(long chatId, CancellationToken ct)
    {
        try
        {
            var lang = _loc.GetLanguage(chatId);
            var text = lang.StartsWith("ru") ? "Открыть сканер" : "Open Scanner";
            var url = _appUrl.TrimEnd('/');
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                url = "https://" + url;
            await _bot!.SetChatMenuButton(chatId, new MenuButtonWebApp
            {
                Text = text,
                WebApp = new WebAppInfo { Url = url },
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set menu button for chat {ChatId}", chatId);
        }
    }

    private async Task HandleStart(long chatId, long fromId, User tgUser, CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<Core.Contracts.IUserRepository>();
        var user = await userRepo.GetByTelegramIdAsync(fromId, ct);

        if (user is not null)
        {
            await userRepo.UpdateChatIdAsync(user.Id, chatId, ct);

            if (user.Language is not null)
            {
                _loc.SetLanguage(chatId, user.Language);
                await SetMenuButtonAsync(chatId, ct);
            }

            switch (user.Status)
            {
                case "new":
                    await userRepo.CompleteRegistrationAsync(user.Id, ct);
                    await ShowMainMenu(chatId, ct);
                    return;
                case "otp":
                    await userRepo.CompleteRegistrationAsync(user.Id, ct);
                    await ShowMainMenu(chatId, ct);
                    return;
                case "active":
                    await ShowMainMenu(chatId, ct);
                    return;
            }
        }

        if (user is null)
        {
            var currentLang = _loc.GetLanguage(chatId);
            var langBtns = _loc.GetOtherLanguages(chatId)
                .Select(l => InlineKeyboardButton.WithCallbackData(l.Label, $"lang_{l.Code}"))
                .ToArray();

            await DeletePreviousScreen(chatId, ct);

            var keyboard = new InlineKeyboardMarkup([
                ..(langBtns.Length > 0 ? new[] { langBtns.ToArray() } : Array.Empty<InlineKeyboardButton[]>()),
                [InlineKeyboardButton.WithUrl(_loc.Get(currentLang, "open_app_btn"), _appUrl)],
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
            {
                _loc.SetLanguage(chatId, user.Language);
                await SetMenuButtonAsync(chatId, ct);
            }
            await ShowMainMenu(chatId, ct);
        }
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
        await DeletePreviousScreen(chatId, ct);
        var msg = await _bot!.SendMessage(
            chatId: chatId,
            text: _loc.Get(chatId, "main_menu"),
            replyMarkup: new InlineKeyboardMarkup([
                [InlineKeyboardButton.WithUrl(_loc.Get(chatId, "open_app_btn"), _appUrl)],
            ]),
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
            [InlineKeyboardButton.WithCallbackData("Главное меню", "main_menu")],
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
