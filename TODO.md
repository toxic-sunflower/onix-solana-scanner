# TODO

Единый список задач/гэпов проекта. Заменяет ROADMAP.md, FIXME.md, GAPS.md
(слиты 2026-07-23 — было дублирование между тремя файлами).

## Критическое

*(пусто — всё закрыто в этом заходе, см. "Сделано")*

## Важное

*(пусто — всё закрыто в этом заходе, см. "Сделано")*

## Среднее

- [ ] **Telegram bot token не шифруется** — берётся из конфигурации/env в
      открытом виде. Ожидаемо для серверного секрета вне БД, но стоит явно
      зафиксировать как решение, а не пробел.

## Продуктовые фичи

### Security & Recovery
- [ ] **Fallback-доступ к админке** — резервный email + TOTP, exclude-коды,
      доверенные устройства (по аналогии с платёжными системами).
      **Сознательно отложено, не забыто**: логин/пароль `Onix.Scanner.Admin`
      теперь берутся из `ADMIN_USERNAME`/`ADMIN_PASSWORD` (env/GitHub Secrets,
      см. "Сделано" ниже) — это уже не временная заглушка, решение принято
      осознанно (см. пункт про Telegram bot token выше — тот же принцип:
      простой секрет вне БД, а не пробел). TOTP/email/доверенные устройства
      сверху — по-прежнему отдельная задача на будущее, не блокер прямо
      сейчас.

## Сделано

- [x] **Метрики** (ТЗ п.19.2) — OpenTelemetry + Prometheus exporter,
      `/metrics` эндпоинт (`Program.cs`, `AddOpenTelemetry().WithMetrics(...)`).
      Не проксируется через `frontend/nginx.conf` (только `/api/` и `/hubs/`),
      значит наружу не торчит — Prometheus скрейпит напрямую по имени
      контейнера внутри docker-compose сети. Кастомные счётчики
      (`Onix.Scanner.Core.Metrics`): Jupiter success/rate-limited/error/
      skipped-backoff, BingX reconnects, Telegram signals sent, spread ticks
      written — плюс стандартная ASP.NET Core/HttpClient/Runtime
      инструментация из коробки. Пакет экспортера
      (`OpenTelemetry.Exporter.Prometheus.AspNetCore`) на момент установки
      доступен только как `-beta.1` (1.17.0-beta.1) — так у OTel давно и
      везде, включая прод-использование, не блокер.
- [x] **Логаут спред в боте.** `TelegramNotificationService.ProcessAlertsAsync`
      — при rearm (спред падает обратно ниже порога) отправляется сообщение
      `logout_title`, но только если до этого реально было отправлено
      сигнальное сообщение (`sub.LastSignalAt != null`), а не на каждый тик
      ниже порога.
- [x] **Настройки уведомлений в боте.** Команда `/settings` — показывает
      текущий порог/cooldown/статус уведомлений, кнопка-тумблер
      `toggle_notifications` (пишет в `UserSettings` напрямую), кнопка
      "Open full settings" на полный Mini App. **Поправка** (изначально тут
      было неверно написано "выбор токенов/кастомных пар там уже есть" — это
      было неправдой, только что закрыто отдельным пунктом ниже, см. "Per-
      token Telegram alert config").
- [x] **Per-token Telegram alert config (Mini App).** Реальный пробел,
      который нашёл пользователь: `UserToken.TelegramEnabled`/
      `AlertThresholdPct` существовали в БД и использовались в
      `GetSubscribersAsync`, но не было ни эндпоинта, ни UI их редактировать
      — только глобальный порог в `/settings`. Добавлено:
      `PATCH /api/v1/user-tokens/{tokenId}/telegram`
      (`{telegramEnabled?, alertThresholdPct?}`), `ITokenRepository.
      SetUserTokenTelegramSettingsAsync`/`GetUserTokenAlertSettingsAsync`.
      На `/favorites` — 🔔/🔕 тумблер на каждой карточке + инлайн-поле
      порога (`≥ X%`), сохраняется по blur. `Dashboard`/`Blacklist` не
      получили эти пропсы (там нет своей `user_tokens`-строки на токен вне
      избранного — не имеет смысла).
- [x] **Удалить аккаунт из бота.** Команда `/deleteaccount` — inline
      confirm/cancel, вызывает существующий `IUserRepository.DeleteAsync`
      напрямую (без похода в Mini App).
- [x] **Настройки (Mini App) — язык.** `PATCH /api/v1/auth/me`
      (`{language}`), персистится на `User.Language`, используется и ботом
      (`_loc.SetLanguage`). Тема/уведомления — уведомления уже были в
      Settings Page (порог/cooldown/telegram toggle), тема — см. пункт ниже.
- [x] **Удалить аккаунт (Mini App).** `DELETE /api/v1/auth/me` +
      confirm-diалог в `Settings.tsx`. `UserRepository.DeleteAsync` теперь
      реально чистит **все** связанные таблицы (`UserTokens`,
      `BlacklistedTokens`, `UserPreferences`, `RefreshTokens`,
      `BlacklistedJtis`, `BackupCodes`, `UserSettings`) — раньше удалялась
      только строка `Users`, остальное осиротевало молча (в БД не было ни
      одного FK на `users.id`, так что это не бросалось ошибкой, просто
      копилось мусором).
- [x] **Внешний вид — настройка колонок на dashboard** (частично, см. ниже).
      `Dashboard.tsx` — кнопка "⚙ Columns", чекбоксы show/hide для
      имени токена, mint-адреса, ссылок на биржи, recent log — персистится в
      `localStorage`. **Полная кастомизация темы (светлая/тёмная) сознательно
      не сделана** — это отдельный, большой рефакторинг: весь фронтенд
      захардкожен на конкретные hex-цвета (`bg-[#16171d]` и т.п.) в каждом
      компоненте, не через CSS-переменные/Tailwind dark-strategy. Переделка
      под настоящую тему — самостоятельная задача, а не довесок к этому
      заходу; выполнена только часть про колонки, о чём явно пишу здесь, а не
      молчу.
- [x] **Что если потерял Telegram?** — резервные коды. `BackupCode`
      (новая таблица, `CodeHash` уникальный индекс, single-use — при
      успешном логине запись удаляется, отдельного флага `Used` не нужно).
      `POST /api/v1/auth/backup-codes/generate` (10 кодов, plaintext
      возвращается один раз, хранится только SHA-256), `GET .../count`,
      `POST /api/v1/auth/backup-codes/login` (`AllowAnonymous`, алтернативный
      логин без Telegram). Фронтенд: генерация/просмотр в `Settings.tsx`,
      форма восстановления на `Landing.tsx` ("Lost access to Telegram?").
      Email как альтернативный канал сознательно не делался — в приложении
      сейчас вообще нет сбора email ни для одного пользователя, добавление
      email потребовало бы SMTP/верификации — отдельная задача больше по
      объёму, чем сами коды.
- [x] BingX WebSocket — Ask 1, depth10@100ms, ping-pong, reconnect
- [x] Jupiter Price API v3 → Quote API (`Buy Price = inAmount / outAmount`,
      учёт `Token.JupiterInputDecimals`, `TokenQuoteAmount.QuoteAmount`)
- [x] SpreadCalculator — формула, статусы, QualityStatus; дедуп единой
      реализации во всех местах (`Core.SpreadCalculator.CalculateSpread`)
- [x] Per-token proxy — HTTP/SOCKS5, шифрование паролей AES-256-CBC
- [x] Web Dashboard — карточки, сортировка, фильтр статусов
- [x] Chart Page — OHLC 5m/15m/1h, Lightweight Charts v5. Live SSE-обновление
      текущего бара + line-серии (не только one-shot REST). Ленивая подгрузка
      истории при скролле к краю (до границы retention 72ч). Tooltip
      time/O/H/L/C/samples (ТЗ п.12.2, через `subscribeCrosshairMove`).
      Кнопка "Reset scale" (ТЗ п.12.3 "возможность reset scale"). Пропуски
      данных НЕ заполняются фиктивными свечами — ТЗ п.12.3 прямо запрещает
      ("при samples = 0 свеча отсутствует"). Timezone-селектор (UTC/Moscow/
      London/New York/Tokyo/Shanghai) — бакетинг остаётся в UTC на сервере
      (без DST-неоднозначностей), конвертация только в отображении
      (`tickMarkFormatter`/tooltip через `Intl.DateTimeFormat`).
- [x] JupiterWorkerService — независимый persistent-цикл на каждый токен
      (ТЗ п.7.1 "один токен = один независимый worker"), супервизор раз в
      секунду батчем обновляет токены/прокси/суммы без роста нагрузки на БД.
      Per-token 429-бэкофф (не морозит всю shared-группу). Observability:
      per-token last-success + раз в минуту сводка в лог (enabled count,
      sweep/db-refresh ms, ok/rate-limited/errored/skipped, топ-5 самых
      "протухших" токенов по символу).
- [x] Settings Page — порог, cooldown, timezone
- [x] Admin — CRUD токенов/прокси, [AdminAuthorize]
- [x] Rate limiting — Jupiter (2s + backoff), API (100 req/min)
- [x] Encryption — AesEncryptionService, пароли прокси
- [x] Chart endpoint — без TimescaleDB, to_timestamp + array_agg
- [x] .NET Aspire AppHost — PostgreSQL контейнер, Dashboard
- [x] Решение .sln + .slnx — Rider, VS, CLI
- [x] Proxy Test (`POST /admin/proxies/{id}/test`) — реальная проверка через
      прокси к Jupiter API, latency, обновление `Proxy.Status/LatencyMs`.
      Перепроверено — не регрессировало, логика (`ProxyTester`) не тронута,
      только перенесена из `Api.Services` в `Core` (без внешней ASP.NET
      Core-зависимости — самодостаточный статический класс), чтобы админка
      могла её переиспользовать без ссылки на весь Api-проект.
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
- [x] **Коллизия тикеров Jupiter↔BingX (баг AVA, +1899% спред).**
      Подтверждено вживую: BingX AVA-USDT и Jupiter-токен AVA, скорее всего,
      разные проекты — совпал только тикер. ТЗ п.5 прямо требует: при
      неоднозначном тикере токен не должен запускаться автоматически,
      статус "Mapping Required", подтверждение вручную. Реализовано:
      `Token.RequiresMapping` (новое поле), `TokenSyncService` больше не
      авто-enable'ит новые CEX-совпадения — только `RequiresMapping=true,
      Enabled=false`. `TokenRepository.UpsertBatchAsync` больше не
      перезаписывает `Enabled`/`RequiresMapping` при повторном sync'е
      (admin-owned после первого создания записи; авто-отключение остаётся
      только если токен реально делистнут с BingX).
      `SpreadCalculator.ComputeStatus` возвращает `MappingRequired` первым
      делом. В админке (`Tokens.razor`) — секция "требуют подтверждения" с
      кнопками Confirm/Reject
      (`POST /admin/tokens/{id}/confirm-mapping|reject-mapping`).
- [x] **Proxy Strict/Fallback policy** (ТЗ 8.3). `Token.ProxyFallbackPolicy`
      (`Strict` по умолчанию — ТЗ прямо требует не переходить на shared IP
      незаметно). `JupiterWorkerService.FetchAndApplyAsync` — при
      реальном сбое прокси (не 429, не кривой ответ Jupiter — это не
      прокси-ошибка) и политике `FallbackToSharedIp` делает один retry через
      shared IP. Редактируется в админке (Tokens.razor, колонка "Fallback
      policy").
- [x] **DELETE-эндпоинты** для токенов и прокси в `AdminController`
      (`DELETE /admin/tokens/{id}`, `DELETE /admin/proxies/{id}`) — оба
      репозитория уже поддерживали `DeleteAsync`, не хватало только роутов.
- [x] **Realtime alert-порог** — веб-алерт (визуальный 🚨 на `TokenCard`)
      теперь берёт `MinimalSpreadPct` из `GET /api/v1/settings` (тот же
      источник правды, что и настройки пользователя), а не захардкоженный
      `SpreadCalculator.DefaultAlertThresholdPct`. `token.alert` SSE-событие
      как было — фронтенд его и раньше не слушал (проверено), поэтому
      исправление сделано клиентски по `token.quote`, без риска сломать
      широковещательную SSE-группу.
- [x] **Timezone selector на графике** (ТЗ п.12.2) — см. пункт Chart Page выше.
- [x] **Фильтр «спред выше X»** (ТЗ п.11.3) — числовое поле на Dashboard
      рядом с All/Positive spread.
- [x] **API документация** — [API.md](API.md), по каждому контроллеру:
      метод, путь, требуемая авторизация, назначение, нетривиальное
      поведение.
- [x] **Мёртвый код `LoginToken`** — модель, `DbSet`, EF-конфигурация,
      `CreateLoginTokenAsync`/`ConsumeLoginTokenAsync` убраны (ни одного
      реального вызова не было — только определения). Миграция
      `DropLoginTokens`.
- [x] **Per-token BingX state** (ТЗ п.6.3) — `last_message_at`/
      `last_ask_price` и так жили в snapshot pool per-token; добавлено то,
      чего не было: `connected`/`reconnect_count` на уровне соединения
      (один multiplexed WebSocket на все символы — TZ хочет "независимое
      состояние" per pair, но физически это одно соединение, так что
      connected/reconnects по природе на уровне соединения, а не пары) +
      раз в минуту сводка в лог (searchable), тот же паттерн, что и у
      Jupiter-воркера.
- [x] **Админка (`Onix.Scanner.Admin`, Blazor Server).** Реально
      существует и работает, проверено локально (Docker Postgres + два
      `dotnet run`): логин по паролю (cookie-auth), страница
      Tokens (Mapping Required confirm/reject, enable-toggle, proxy
      assignment, fallback policy, quote amount, ручное
      добавление/удаление, диагностика ticks/1h из БД напрямую), страница
      Proxies (CRUD + Test, пароли шифруются через тот же
      `IProxyRepository`/`AesEncryptionService`, что и основной API),
      страница Settings (read-only обзор Jupiter/BingX/Freshness/Telegram/
      Storage — большинство значений заданы в коде/env осознанно, не
      DB-backed рантайм-конфиг). Добавлен в `Onix.Scanner.slnx`.
- [x] **Логин админки — env/GitHub Secrets вместо хардкода.**
      `Onix.Scanner.Admin/Program.cs` больше не содержит пароль в исходниках
      — `ADMIN_USERNAME`/`ADMIN_PASSWORD` читаются из конфига (тот же
      `.env`-парсинг, что и в основном Api `Program.cs`, маппится в
      `Admin:Username`/`Admin:Password`; в проде — те же имена через GitHub
      Secrets, по аналогии с `TELEGRAM_BOT_TOKEN`/`ENCRYPTION_KEY` в
      `deploy.yml`). Сравнение логина/пароля через
      `CryptographicOperations.FixedTimeEquals` (не `==`) — защита от
      timing-атаки на подбор пароля посимвольно. Раз хардкода в исходниках
      больше нет, `src/Onix.Scanner.Admin/` убран из `.gitignore` — можно
      коммитить. **Что нужно сделать вручную (я не могу — нет `gh` CLI в
      этом окружении)**: добавить в GitHub repo secrets `ADMIN_USERNAME` и
      `ADMIN_PASSWORD` (значения — см. чат, пароль сгенерирован). Локально
      уже лежат в `.env` (не в git).
- [x] **Админка подключена к деплою (код/конфиг).** `src/Onix.Scanner.Admin/
      Dockerfile` (без npm-стадии — Blazor Server, серверный рендеринг,
      отдельная сборка фронтенда не нужна). `docker-compose.yml` — новый
      сервис `admin`, отдельный от blue/green (внутренний инструмент,
      короткий рестарт при деплое не страшен), порт 5050, те же
      `Admin__Username`/`Admin__Password`/`Encryption__Key`/
      `ConnectionStrings__Default`, что и у `app_blue`/`app_green`.
      `deploy.yml` — билдит и рестартит `admin` после blue/green-свитча
      основного приложения; если healthcheck админки не прошёл, это **не**
      роняет весь деплой (публичное приложение не затронуто), только пишет
      логи в вывод экшена. `ADMIN_USERNAME`/`ADMIN_PASSWORD` добавлены в
      список `envs:`/`env:` в `deploy.yml` рядом с остальными секретами.
      Rate-limit на логин (5 попыток/мин на IP, `RequireRateLimiting("login")`)
      — единственная защита от подбора пароля на сейчас, не замена 2FA/
      файрволу. `ADMIN_USERNAME`/`ADMIN_PASSWORD` добавлены в GitHub repo
      secrets (сделано вручную). **Открытый блокер — см. "Критическое"
      выше**: порт 5050 всё ещё не закрыт файрволом на сервере, это
      инфраструктурная настройка вне репозитория, я её сделать не могу.
