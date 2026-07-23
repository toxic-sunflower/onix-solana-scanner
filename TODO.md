# TODO

Единый список задач/гэпов проекта. Заменяет ROADMAP.md, FIXME.md, GAPS.md
(слиты 2026-07-23 — было дублирование между тремя файлами).

## Критическое

- [ ] **Proxy Test заглушка вернулась/не проверено после последних правок** —
      `POST /admin/proxies/{id}/test` — реализация есть (`ProxyTester.cs`),
      сверить что не регрессировало.
- [ ] **Realtime alert-порог (`token.alert`) захардкожен** на 5%
      (`SpreadCalculator.DefaultAlertThresholdPct`) вместо пользовательского
      `MinimalSpreadPct`/`AlertThresholdPct` — веб-алерт и Telegram-алерт могут
      расходиться по порогу для одного и того же пользователя.

## Важное

- [ ] **Timezone selector на графике** (ТЗ п.12.2) — `ChartPage` не передаёт
      пользовательскую timezone в запрос графика, всегда UTC на сервере, хотя
      `UserSettings.Timezone` существует и настраивается.
- [ ] **Фильтр «спред выше X»** (ТЗ п.11.3) — сейчас только фильтр по статусам.
- [ ] **API документация** — описание всех endpoints.
- [ ] **Proxy Strict/Fallback policy** (ТЗ раздел 8.3) — нужна настройка
      "Strict Proxy" vs "Fallback to Shared IP" на уровне токена/прокси; поля
      в модели `Proxy`/`Token` пока нет. Сейчас при ошибке прокси воркер просто
      помечает токен `ProxyError`, fallback на shared IP не происходит
      (прокси жёстко привязана к токену через `Token.ProxyId`). Нужно завести
      поле и обсудить дефолт.
- [ ] **DELETE-эндпоинты** для токенов и прокси в `AdminController`
      (сейчас только Create/Patch/GetAll).

## Среднее

- [ ] **Метрики** — Prometheus/OpenTelemetry (ТЗ п.19.2).
- [ ] **Mint Address change logging** (ТЗ п.5.3).
- [ ] **Per-token BingX state** — connected, last_message_at, reconnect_count.
- [ ] **Telegram bot token не шифруется** — берётся из конфигурации/env в
      открытом виде. Ожидаемо для серверного секрета вне БД, но стоит явно
      зафиксировать как решение, а не пробел.
- [ ] **Мёртвый код `LoginToken`** (модель/таблица/репозиторий:
      `CreateLoginTokenAsync`/`ConsumeLoginTokenAsync`) — не часть текущего
      auth-флоу (OAuth+PKCE), стоит убрать отдельным заходом.

## Продуктовые фичи (не начато)

### Telegram Bot
- [ ] **Логаут спред в боте** — уведомление при logout-спреде (CEX цена уходит
      за границы DEX).
- [ ] **Настройки уведомлений** — выбор токенов для мониторинга, пороги
      спреда, кастомные торговые пары.
- [ ] **Удалить аккаунт из бота** — кнопка удаления данных пользователя без
      входа в Mini App.

### Mini App / UI
- [ ] **Настройки** — страница управления профилем: язык, тема, уведомления.
- [ ] **Внешний вид** — кастомизация темы (светлая/тёмная), настройка колонок
      на dashboard.
- [ ] **Удалить аккаунт** — кнопка полного удаления аккаунта и всех данных.

### Security & Recovery
- [ ] **Что если потерял Telegram?** — восстановление доступа через
      резервные коды/email при регистрации.
- [ ] **Админка** — веб-панель для управления пользователями/токенами,
      мониторинг системы (частично есть `AdminController` — оценить, чего не
      хватает для полноценной веб-панели).
- [ ] **Fallback-доступ к админке** — резервный email + TOTP, exclude-коды,
      доверенные устройства (по аналогии с платёжными системами).

## Сделано

- [x] BingX WebSocket — Ask 1, depth10@100ms, ping-pong, reconnect
- [x] Jupiter Price API v3 → Quote API (`Buy Price = inAmount / outAmount`,
      учёт `Token.JupiterInputDecimals`, `TokenQuoteAmount.QuoteAmount`)
- [x] SpreadCalculator — формула, статусы, QualityStatus; дедуп единой
      реализации во всех местах (`Core.SpreadCalculator.CalculateSpread`)
- [x] Per-token proxy — HTTP/SOCKS5, шифрование паролей AES-256-CBC
- [x] SignalR — token.quote, token.status, token.alert, version + event_id
- [x] Web Dashboard — карточки, сортировка, фильтр статусов, SignalR
- [x] Chart Page — OHLC 5m/15m/1h, Lightweight Charts v5
- [x] Settings Page — порог, cooldown, timezone
- [x] Admin — CRUD токенов/прокси, [AdminAuthorize]
- [x] Rate limiting — Jupiter (2s + backoff), API (100 req/min)
- [x] Encryption — AesEncryptionService, пароли прокси
- [x] Chart endpoint — без TimescaleDB, to_timestamp + array_agg
- [x] .NET Aspire AppHost — PostgreSQL контейнер, Dashboard
- [x] Решение .sln + .slnx — Rider, VS, CLI
- [x] Proxy Test (`POST /admin/proxies/{id}/test`) — реальная проверка через
      прокси к Jupiter API, latency, обновление `Proxy.Status/LatencyMs`
- [x] Статус `ProxyError` — `TokenSnapshot.ProxyErrorUntilUtc`, TTL 30с,
      проверяется первым в `SpreadCalculator.ComputeStatus`
- [x] Изоляция per-token воркеров в `JupiterWorkerService` — независимый
      async `Task` на каждый токен вместо батчинга по прокси-группе
- [x] `GET /api/v1/health` — публичный, без авторизации, для liveness/readiness
- [x] Debug-эндпоинт `GET /api/v1/tokens/debug/snapshots` — удалён
- [x] Telegram cooldown/rearm — персистентно в `user_tokens`
      (`LastSignalAt`, `IsArmed`), ТЗ п.13.4
- [x] Аутентификация — "Log In With Telegram" OAuth 2.0 + PKCE
      (убраны bot deep-link флоу и легаси HMAC-виджет)
- [x] **Переход на вебхуки** — `SetWebhook` вместо long-polling
      (`TelegramNotificationService`, `TelegramWebhookController`)
- [x] **Token.Status** — считается live из snapshot pool
      (`SpreadCalculator.ComputeStatus`) вместо чтения замороженной колонки в БД
- [x] **docker-compose.yml** — production deployment, blue/green
      (`app_blue`/`app_green`, health-gated nginx switch)
- [x] **CI/CD** — `.github/workflows/deploy.yml` (blue/green деплой),
      `logs.yml` (просмотр логов, поиск по всей истории),
      `server-control.yml` (start/stop/restart/logs/status активного сервиса)
- [x] **Docker** — контейнеризация, `curl` в рантайм-образе для healthcheck
- [x] **Авто-миграции БД при старте** (ТЗ п.20.2) — `MigratorService`,
      fail-fast при неудаче вместо молчаливого продолжения на несовпадающей
      схеме
- [x] **HTTPS** — nginx reverse-proxy, терминация TLS
- [x] **example.env** / секреты — GitHub Secrets → env vars → генерируемый на
      сервере `.env`, не трекается в git
- [x] **Избранное и Чёрный список.** Две новые вкладки. Избранное = уже
      существовавшая таблица `user_tokens` (раньше писалась бэкендом, но
      фронтенд её не дёргал) — теперь `Dashboard` умеет добавлять/убирать
      токен из избранного (⭐), страница `/favorites` показывает список,
      можно убрать из избранного или сразу отправить в чёрный список.
      Чёрный список — новая таблица `blacklisted_tokens` (per-user), новый
      `BlacklistController` (`GET/POST/DELETE /api/v1/blacklist/{tokenId}`).
      Токены из чёрного списка: не отображаются в `GET /api/v1/tokens`
      (Dashboard), не могут быть добавлены в избранное (400 на
      `POST /api/v1/user-tokens`), при добавлении в ЧС автоматически
      убираются из избранного. На странице `/blacklist` — только "Restore".
      Пин работает в Dashboard и Favorites (общее поле `IsPinned` в
      `user_tokens`). Миграция `AddBlacklistedTokens`.
- [x] **Автосортировка Dashboard по спреду (убывание).** `Dashboard.tsx` —
      список токенов сортируется по `spreadPct` desc (закреплённые — всегда
      первыми), пересчитывается на каждое обновление `allTokens`, в т.ч. на
      каждый SSE `token.quote`.
- [x] **SignalR → SSE.** `SpreadHub` удалён, заменён на
      `GET /api/v1/sse/spread` (`SseController` + `SseBroadcaster`), группы
      premium/free сохранены. Токен передаётся query-параметром
      `access_token` (`EventSource` не умеет кастомные заголовки). Фронтенд:
      `lib/signalr.ts` → `lib/sse.ts` (ручной реконнект с обновлением токена
      через `ensureFreshToken`, т.к. нативный автореконнект `EventSource` бы
      слал протухший токен вечно). `@microsoft/signalr` убран из
      `package.json`.
