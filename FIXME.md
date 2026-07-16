# Доработки по ТЗ (v1.0)

## Выполнено
- [x] BingX WebSocket — Ask 1, depth10@100ms, ping-pong, reconnect
- [x] Jupiter Price API v3 — батчинг всех токенов в одном запросе
- [x] SpreadCalculator — формула, статусы, QualityStatus
- [x] Per-token proxy — HTTP/SOCKS5, шифрование паролей AES-256-CBC
- [x] SignalR — token.quote, token.status, token.alert, version + event_id
- [x] Web Dashboard — карточки, сортировка, фильтр статусов, SignalR
- [x] Chart Page — OHLC 5m/15m/1h, Lightweight Charts v5
- [x] Settings Page — порог, cooldown, timezone
- [x] Telegram — per-user алерты, inline кнопки, /start auth
- [x] Admin — CRUD токенов/прокси, [AdminAuthorize]
- [x] Rate limiting — Jupiter (2s + backoff), API (100 req/min)
- [x] Encryption — AesEncryptionService, пароли прокси
- [x] Chart endpoint — без TimescaleDB, to_timestamp + array_agg
- [x] UserSecrets — Onix.Scanner.Api, appsettings.json очищен
- [x] .gitignore — secrets, bin, obj, node_modules
- [x] .NET Aspire AppHost — PostgreSQL контейнер, Dashboard
- [x] Решение .sln + .slnx — Rider, VS, CLI

## Осталось по ТЗ

### Критическое
- [ ] **rearm_below_threshold** — сигнал повторно только после ухода ниже порога и нового пересечения (сейчас time-based cooldown)
- [ ] **Proxy Test** — `POST /admin/proxies/{id}/test` возвращает заглушку

### Важное
- [ ] **docker-compose.yml** — production deployment (API + PostgreSQL + frontend)
- [ ] **example.env** — файл без секретов
- [ ] **Инструкция деплоя** — dev/stage/prod
- [ ] **API документация** — описание всех endpoints
- [ ] **Timezone selector на графике** — п.12.2 ТЗ
- [ ] **Фильтр «спред выше X»** — п.11.3 ТЗ (сейчас только статусы)

### Среднее
- [ ] **HTTPS** — reverse-proxy (Caddy/nginx) — п.18 ТЗ
- [ ] **Метрики** — Prometheus/OpenTelemetry — п.19.2 ТЗ
- [ ] **Mint Address change logging** — п.5.3 ТЗ
- [ ] **Авто-миграции БД при старте** — п.20.2 ТЗ
- [ ] **Per-token BingX state** — connected, last_message_at, reconnect_count
- [ ] **Strict Proxy / Fallback policy** — настройка поведения при ошибке прокси
