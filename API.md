# API Reference

All endpoints are under `/api/v1`. Auth is JWT bearer, sent as either the
standard `Authorization: Bearer <token>` header, the `X-Auth-Token` header
(used by the frontend's `authFetch`), or `?access_token=` query param (used
by SSE, since `EventSource` can't set custom headers). Tokens are short-lived
(15 min) access tokens + rotating refresh tokens; see [Auth](#auth).

Unless noted **AllowAnonymous**, every endpoint requires a valid access
token. Endpoints marked **Admin** additionally require the `Admin` role.

## Auth

Sole login path is "Log In With Telegram" (OAuth 2.0 + PKCE,
`core.telegram.org/bots/telegram-login`) — see `AuthController.cs` for the
full flow description.

| Method | Path | Auth | Description |
|---|---|---|---|
| POST | `/auth/openid` | AllowAnonymous | Exchange `{code, codeVerifier}` (PKCE) for a session. Backend exchanges the code for an id_token, validates it, creates/updates the user, issues `{token, refreshToken}`. Also best-effort starts the Telegram bot dialog for the user. |
| GET | `/auth/check` | required | Returns `{userId, telegramId, role, tier, demoSecondsUsed, demoQuotaSeconds, hasPaidAccess}`. Used to validate a token is still good and to detect if the demo quota ran out. |
| POST | `/auth/refresh` | AllowAnonymous | `{refreshToken}` → new `{token, refreshToken}`. Old refresh token is invalidated (rotation). |
| POST | `/auth/revoke` | required | Log out the current session (`{refreshToken}`). |
| POST | `/auth/revoke-all` | required | Log out every session for this user. |
| POST | `/auth/revoke-others` | required | Log out every session except the current one (`{refreshToken}` identifies "current"). |
| GET | `/auth/sessions` | required | List active sessions (`?currentRefreshToken=` to flag which one is "you"). |
| DELETE | `/auth/sessions/{id}` | required | Revoke one specific session. |
| GET | `/auth/me` | required | Basic profile: id, Telegram id, display name, role. |

## Tokens (public catalog + per-token data)

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/tokens?q=&cexOnly=&offset=&take=` | required | Search/list all tokens with live price/spread/status. `cexOnly=true` also drops the caller's blacklisted tokens. Pinned-first, then by spread desc. |
| GET | `/tokens/{id}` | required | One token's current card (price, spread, status, links). |
| GET | `/tokens/{tokenId}/chart?interval=5m\|15m\|1h&from=&to=&timezone=` | required | OHLC spread candles. `samples=0` buckets are omitted entirely (never gap-filled — see TZ 12.3). `timezone` is currently echoed in the response but bucketing itself is always UTC; client-side display conversion is the frontend's job. |
| GET | `/tokens/{tokenId}/ticks?limit=` | required | Raw recent ticks (time/spreadPct/bingxPrice/jupiterPrice), most recent first. |

## Realtime (SSE)

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/sse/spread` | required (query token) | `text/event-stream`. Sends a full snapshot of every enabled token on connect, then live `token.quote` / `token.status` / `token.alert` events as they happen. Premium/Free tier gets its own broadcast group (Free gets every 10th cycle only). |

## Favorites (per-user watchlist)

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/user-tokens` | required | The caller's favorited tokens with live price/spread. |
| POST | `/user-tokens` | required | `{tokenId}` — add to favorites. 400 if the token is blacklisted for this user. |
| DELETE | `/user-tokens/{tokenId}` | required | Remove from favorites. |
| PATCH | `/user-tokens/{tokenId}/pin` | required | `{isPinned}` — pin/unpin (shared with Dashboard). |

## Blacklist (per-user)

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/blacklist` | required | Caller's blacklisted tokens. |
| POST | `/blacklist/{tokenId}` | required | Blacklist a token — cascades to remove it from favorites too. |
| DELETE | `/blacklist/{tokenId}` | required | Un-blacklist (does not re-add to favorites). |

## Settings (per-user)

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/settings` | required | `MinimalSpreadPct`, `TelegramNotificationsEnabled`, `CooldownSeconds`, `Timezone`. Auto-creates a default row on first call. |
| PATCH | `/settings` | required | Partial update of the same fields. |

## Config (unauthenticated, needed to build the login URL)

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/config` | AllowAnonymous | `{botUsername, oauthClientId, oauthAuthorizationEndpoint, oauthRedirectUri}` — everything the frontend needs to construct the Telegram OAuth authorize URL. No secrets (client_secret never leaves the backend). |

## Health

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/health` | AllowAnonymous | `{status: "ok", timestamp}`. Used by the blue/green deploy healthcheck. |

## Telegram webhook (Telegram → us, not for frontend use)

| Method | Path | Auth | Description |
|---|---|---|---|
| POST | `/telegram/webhook` | AllowAnonymous (validated via `X-Telegram-Bot-Api-Secret-Token` header) | Receives Telegram bot updates (messages, callback queries). |

## Admin (all require the `Admin` role)

| Method | Path | Description |
|---|---|---|
| GET | `/admin/tokens` | Full token list (unfiltered, includes disabled/Mapping Required). |
| POST | `/admin/tokens` | Create a token manually. Manually-created tokens skip Mapping Required (an admin typing in the mint address by hand is itself the confirmation). |
| PATCH | `/admin/tokens/{id}` | Partial update — accepts `symbol`, `name`, `solanaMint` (change is logged — TZ 5), `bingxSymbol`, `jupiterInputMint`, `jupiterInputDecimals`, `quoteAmount`, `bingxUrl`, `jupiterUrl`, `solscanUrl`, `enabled`, `telegramEnabled`, `proxyId`, `proxyFallbackPolicy`. |
| DELETE | `/admin/tokens/{id}` | Delete a token. |
| POST | `/admin/tokens/{id}/confirm-mapping` | Admin confirms a Mapping Required token is the right project — clears the gate, enables monitoring. |
| POST | `/admin/tokens/{id}/reject-mapping` | Admin rejects it — clears the gate without enabling. |
| GET | `/admin/proxies` | List all proxies (passwords decrypted for display). |
| POST | `/admin/proxies` | Create a proxy (password encrypted at rest). |
| DELETE | `/admin/proxies/{id}` | Delete a proxy. |
| POST | `/admin/proxies/{id}/test` | Live-test a proxy against Jupiter's price API, records latency/status. |

## Notes for anyone extending this

- `SpreadCalculator.CalculateSpread`/`ComputeStatus` in `Onix.Scanner.Core`
  is the *only* place spread/status math should live — every controller and
  the SSE broadcaster call into it rather than reimplementing the formula.
- Response bodies are camelCase JSON (`JsonStringEnumConverter` for enums)
  by ASP.NET Core's default policy — C# `PascalCase` properties become
  `camelCase` on the wire automatically, no manual mapping needed.
- The full OpenAPI/Swagger document (machine-readable) is available at
  `/openapi/v1.json` in Development environment via `AddOpenApi()` — this
  document is the human-oriented companion to that, focused on *why* an
  endpoint exists and any non-obvious behavior, not just its shape.
