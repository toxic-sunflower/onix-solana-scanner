# Gaps: соответствие проекта TZ.md

Список расхождений между текущей реализацией и техническим заданием (`TZ.md`),
найденных при аудите 2026-07-22. Статус обновляется по мере правок.

## Исправлено

- **Jupiter Buy Price считалась по споту, а не по котировке на `quote_amount`.**
  `JupiterWorkerService` брал цену из Price API v3 (индикативный spot),
  `quote_amount` нигде не использовался. Переписано на Jupiter Quote API
  (`GET .../swap/v1/quote?inputMint=&outputMint=&amount=&slippageBps=`),
  `Buy Price = inAmount / outAmount` с учётом `Token.JupiterInputDecimals`
  (новое поле, дефолт 6) и `TokenQuoteAmount.QuoteAmount`. Соответствует
  формуле из ТЗ п.7.2. **Нужно перед релизом сверить актуальный домен/путь
  Quote API с https://developers.jup.ag/ (см. Приложение A ТЗ).**

- **Статус `ProxyError` не выставлялся.** Добавлено поле
  `TokenSnapshot.ProxyErrorUntilUtc`; при ошибке запроса через
  индивидуальную прокси воркер выставляет TTL (30 сек), `SpreadCalculator.ComputeStatus`
  проверяет его первым. См. `SpreadCalculator.cs`, `JupiterWorkerService.cs`.

- **Изоляция per-token воркеров.** `JupiterWorkerService` раньше батчил все
  токены одной прокси-группы в один HTTP-запрос — ошибка/таймаут одного
  запроса "гасила" обновление всей группы. Переписано на независимый async
  `Task` на каждый токен (без ОС-потоков — это I/O-bound работа), с
  собственным try/catch, таймаутом и rate-limit-паузой. Группировка по прокси
  осталась только для контроля конкурентности/pacing (семафоры), не для
  батчинга самого запроса.

- **`POST /admin/proxies/{id}/test` был заглушкой** (`test_not_implemented`).
  Реализована реальная проверка: запрос через прокси к Jupiter API,
  замер latency, обновление `Proxy.Status/LatencyMs/LastCheckAt`. См.
  `ProxyTester.cs`, `AdminController.cs`.

- **Формула спреда дублировалась в 4 местах** (`TokensController`,
  `UserTokensController`, `SpreadHub`, debug-эндпоинт). Все места переведены
  на единственную реализацию `Onix.Scanner.Core.SpreadCalculator.CalculateSpread`.
  Заодно исправлен баг: `Core`-версия не проверяла `bingxRaw == 0`, из-за чего
  при отсутствии BingX-цены спред мог посчитаться как -100% вместо 0.

- **`GET /health` был доступен только под `[Authorize(Roles="Admin")]`**,
  что не годится для liveness/readiness проб оркестратора. Вынесен в отдельный
  публичный `HealthController` на `GET /api/v1/health` без авторизации,
  как требует ТЗ (раздел 15.1).

- **Debug-эндпоинт `GET /api/v1/tokens/debug/snapshots`** — дамп сырых
  in-memory снапшотов, не описан в ТЗ, был доступен любому залогиненному
  пользователю. Удалён полностью (не было смысла оставлять даже под
  admin-ролью — чисто отладочный код разработчика).

- **Telegram cooldown был простым in-memory таймером без rearm/hysteresis**,
  сбрасывался при рестарте сервиса (риск повторной рассылки сразу после
  деплоя). Заменено на персистентную логику в таблице `user_tokens`
  (новые поля `LastSignalAt`, `IsArmed`): сигнал уходит либо при первом
  пересечении порога снизу вверх (`IsArmed`), либо по истечении cooldown;
  при падении спреда ниже порога состояние "взводится" заново. Соответствует
  ТЗ п.13.4 буквально. См. `TelegramNotificationService.cs`,
  `UserRepository.SetAlertStateAsync`.

- **Аутентификация переведена целиком на "Log In With Telegram" (OAuth 2.0 +
  PKCE, core.telegram.org/bots/telegram-login).** Убраны: bot deep-link флоу
  (`GET /auth/telegram`, `POST /auth/verify`), легаси-виджет с HMAC-хэшем
  (`POST /auth/tg-widget`, `VerifyTelegramHash`), неиспользуемый magic-link
  эндпоинт (`GET /login/{token}` — код никогда не вызывал
  `CreateLoginTokenAsync`, был мёртвым).

  Флоу: фронтенд генерирует PKCE `code_verifier`/`code_challenge`, редиректит
  на `https://oauth.telegram.org/auth` (`client_id`, `redirect_uri`,
  `response_type=code`, `scope=openid profile`, `state`, `code_challenge`).
  Telegram возвращает `code` на `redirect_uri`. Фронтенд шлёт `code` +
  `code_verifier` в `POST /api/v1/auth/openid`. **Только бэкенд**
  (`TelegramOAuthClient`) обменивает `code` на `id_token` через
  `POST https://oauth.telegram.org/token` с `client_secret` (Basic auth) —
  секрет никогда не попадает в браузер. Подпись `id_token` проверяется по
  JWKS Telegram (`TelegramOpenIdValidator`). Telegram ID берётся из claim'а
  **`id`**, а не `sub` (`sub` — непрозрачный OIDC-идентификатор, отличается
  от реального Telegram user ID — это баг первой версии, найден и исправлен).

  Секреты (`ClientId`/`ClientSecret`/`RedirectUri`) заведены через ту же
  схему GitHub Secrets → env vars → генерируемый на сервере `.env`, что и
  остальные (`TELEGRAM_OAUTH_CLIENT_ID/SECRET/REDIRECT_URI`, см.
  `.github/workflows/deploy.yml`).

  Привязка бота (`ChatId`) остаётся автоматической —
  `TelegramNotificationService.HandleStart` уже сопоставляет пользователя по
  Telegram ID при любом `/start`, отдельного шага не требуется.

  Проверено по двум независимым источникам (WebSearch + прямой фетч
  `core.telegram.org/bots/telegram-login`) 2026-07-22 — endpoints, формат
  code exchange и claim'ы `id_token` сошлись в обоих. Первая версия этой
  правки (JWT сразу из виджета, без code exchange) была ошибочной — не
  учитывала, что у Telegram здесь полноценный Authorization Code Flow с
  client_secret, а не самодостаточный подписанный токен от кнопки.

  Мёртвый код `LoginToken`/`CreateLoginTokenAsync`/`ConsumeLoginTokenAsync`
  (модель, таблица, репозиторий) оставлен нетронутым — не был частью
  auth-флоу, который просили заменить; стоит убрать отдельным заходом.

## Открытые гэпы (не тронуто в этом заходе)

- **Realtime-транспорт: SignalR используется для однонаправленного потока
  сервер→клиент** (`token.quote`/`token.status`/`token.alert`), где клиент
  ничего не шлёт обратно. Это оверкилл — по сути подходит Server-Sent Events.
  Переезд требует правки и Hub'а, и фронтенд-подключения (premium/free группы,
  реконнект), поэтому вынесен в отдельную задачу/PR, не делается вместе с
  остальными точечными правками.

- **Realtime alert-порог (`token.alert`) захардкожен** на 5%
  (`SpreadCalculator.DefaultAlertThresholdPct`) вместо пользовательского
  `MinimalSpreadPct`/`AlertThresholdPct`, из-за чего веб-алерт и Telegram-алерт
  могут расходиться по порогу для одного и того же пользователя.

- **Proxy Strict/Fallback policy не реализована.** ТЗ (раздел 8.3) требует
  отдельную настройку "Strict Proxy" vs "Fallback to Shared IP" на уровне
  токена/прокси. В модели `Proxy`/`Token` такого поля нет — сейчас при ошибке
  прокси воркер просто помечает токен `ProxyError`, но не может ни строго
  блокировать переход на shared IP (fallback и так не происходит, потому что
  прокси жёстко привязана к токену через `Token.ProxyId`), ни явно
  переключиться на общий IP по политике — нужно сначала завести это поле и
    обсудить дефолт.

- **Нет DELETE-эндпоинтов** для токенов и прокси в `AdminController`
  (только Create/Patch/GetAll).

- **Telegram bot token не шифруется** — берётся из конфигурации/env в
  открытом виде (ожидаемо для серверного секрета вне БД, но стоит явно
  зафиксировать как решение, а не пробел).

- **ChartPage не передаёт пользовательскую timezone** в запрос графика —
  всегда использует UTC по умолчанию на сервере, хотя `UserSettings.Timezone`
  существует и настраивается.
