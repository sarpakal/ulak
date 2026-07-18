# ROADMAP

## Phase 1 — Transport gateway ✅ Done

- [x] Four channels: SMS (`/api/messages/sms`), Email, WhatsApp, Push
- [x] SMS routing by E.164 prefix (`Sms:ProviderPrefixes`): `+90` → Corvass, `+1` → Twilio
- [x] Mixed-prefix recipient batches split and fanned out per provider
- [x] App-level SMS retry (`Sms:RetryCount` / `RetryDelayMs`) + `SmsException` with provider context
- [x] Polly retry + timeout on all outbound HttpClients (`HttpPolicies`)
- [x] `MessageLogs` persistence (channel, recipient, payload, status, correlation id)
- [x] `CorrelationIdMiddleware` + JSON console logging with scopes
- [x] EF Core migrations applied on startup (`Database.Migrate()`)
- [x] DI consolidated in `MessengerInfrastructureModule`; options pattern throughout
- [x] VPS deployment: `ulak-service` at `https://ulak.akgyh.com` + internal `/messenger/` route
- [x] Namespace cleanup: `AuthApi.Models` → `Messenger.Core.Models` (LESSONS #1)

---

## Phase 2 — Security hardening (open — see [SECURITY.md](SECURITY.md))

- [ ] **Add authentication** — decide between static API key middleware or Auth.Api JWT
      validation per calling service. Still open; the allowlist below is a network-layer
      mitigation, not app-level auth (dev IPs are dynamic — an API key is the better long-term
      fit for dev-machine access).
  - [x] **Nginx IP allowlist on `/api/messages/*`** (2026-07-17). `location /api/messages/`
        in `deploy/nginx/ulak.conf` (mirrored into live `apis.conf`) now `allow`s only
        `172.19.0.0/16` (apps_appnet containers — the workhorse caller), `172.18.0.0/16`
        (n8n), `127.0.0.1`, and the two dev-machine IPs; `deny all` otherwise. Allow set is
        log-derived (2 weeks of access logs), not guessed. `/health` stays public (falls
        through to `location /`). Verified: allowlisted → 200, non-allowlisted → 403, health → 200.
- [ ] Rate limiting on `/api/messages/*` (pattern: Auth.Api's `auth-by-ip` policy)
- [x] Throw on unmatched SMS prefix in production instead of `ConsoleSmsService` fallback
      (silent message loss with HTTP 200 — [Infrastructure LESSONS](Messenger.Infrastructure/LESSONS.md)).
      `RoutingSmsSender.Resolve` now throws `SmsException` on unmatched prefix and on unknown
      provider name; console fallback is gated behind `Sms:AllowConsoleFallback` (dev only,
      default `false` so prod is fail-closed).
- [ ] `MessageLogs` retention job (pattern: Auth.Api `AuditRetentionJob`) — payloads are PII
- [x] Remove dead `Messaging:CorvassApi` config section everywhere (live binding is `Corvass:`).
      Deleted `CorvassApiOptions`, its `Program.cs` binding, the `appsettings.Development.json`
      section, and all doc references ([solution LESSONS](LESSONS.md) #2, [Core LESSONS](Messenger.Core/LESSONS.md) #2).
      Operators must still purge stale `Messaging__CorvassApi__*` keys from the live VPS `.env`.
- [x] Patch `Microsoft.OpenApi` NU1903 (GHSA-v5pm-xwqc-g5wc, high severity). `AspNetCore.OpenApi`
      10.0.7 — and 10.0.9 — transitively pull the vulnerable `2.0.0`, and `2.0.1` is still in
      range. Fixed with a direct override to the latest 2.x (`2.10.0`) in `Messenger.Api`;
      forward-compatible within the major, verified by the integration tests booting `AddOpenApi()`.

---

## Phase 3 — Design debt (open)

- [ ] Move `TwilioClient.Init()` from `TwilioSmsSender` constructor to startup
      (`Program.cs`) — per-request global SDK mutation races under concurrency
- [ ] Extract `IMessageLogService` so `MessagesController` no longer injects
      `MessengerDbContext` directly
- [ ] If a third SMS provider is added: keyed DI (`AddKeyedScoped<ISmsSender, T>("name")`)
      instead of a third concrete field in `RoutingSmsSender`
- [ ] Activate real Twilio sending (`TwilioSmsSender` is a stub pending the Twilio NuGet
      package + credentials)
- [x] Migrate `FcmPushSender` to the FCM HTTP v1 API — the legacy server-key API it used
      (`Authorization: key=…`) was shut down by Google. Now POSTs to
      `/v1/projects/{ProjectId}/messages:send` with an OAuth2 bearer from
      `IFcmAccessTokenProvider` (`GoogleFcmAccessTokenProvider` mints/caches a token from a
      service-account credential via `Google.Apis.Auth`). Options changed:
      `FcmNotificationOptions` now carries `BaseUrl` / `ProjectId` / `CredentialsPath`
      (all optional — the app boots without FCM and fails fast at send time). Requires a
      Google service-account JSON on the VPS (`Messaging__FcmNotification__CredentialsPath`)
      + `Messaging__FcmNotification__ProjectId` — the options bind under `Messaging:FcmNotification`,
      NOT a root `Fcm:` section (the LESSONS #6 wrong-prefix trap).
      Dead `PushNotificationSender` console stub removed.
- [x] FCM push hardening (2026-07-11). The FCM HttpClient moved to
      `Microsoft.Extensions.Http.Resilience` (`AddResilienceHandler` + `HttpRetryStrategyOptions`,
      configured in `HttpPolicies.ConfigureFcmResilience`) — retries on 429/5xx/408/transport
      **honoring `Retry-After` natively**, never retries 400/404 dead-token responses; Corvass/
      WhatsApp stay on the legacy `AddPolicyHandler` policies. `FcmPushSender` now parses the
      FCM v1 error payload (`error.details[].errorCode`) and throws a classified
      `PushSendException` (`Messenger.Core.Exceptions`) instead of a bare `EnsureSuccessStatusCode`.
      `POST /api/messages/push` maps it to an honest contract: **410 Gone** (UNREGISTERED /
      INVALID_ARGUMENT → caller deletes the device token), **429** (+`Retry-After` passthrough),
      **502** (provider fault, incl. SENDER_ID_MISMATCH — a ULAK config problem, not a dead
      token). `MessageLogs` gained a nullable `ErrorCode` column
      (`AddErrorCodeToMessageLog` migration) so dead-token events are queryable on the VPS.
      Consumer-side device-token storage/cleanup (Notifications.Api) is a deliberate follow-up
      built on this contract. Note: `Microsoft.Extensions.*` (non-EF) pins moved 10.0.7 → 10.0.9
      (required by Http.Resilience 10.7.0; EF pins untouched).
- [x] Realign EF Core package versions (the "versions move together" rule). `EFCore.Design`
      `10.0.7` is `PrivateAssets=all` so it never flowed to consumers, who inherited only
      EF Core `10.0.4` via Npgsql's minimum → CS1705/MSB3277 in any EF-touching consumer.
      Fixed by adding explicit **public** `Microsoft.EntityFrameworkCore` +
      `Microsoft.EntityFrameworkCore.Relational` `10.0.7` to `Messenger.Infrastructure`;
      the test-project workaround pins were removed. ([Infrastructure LESSONS](Messenger.Infrastructure/LESSONS.md) #5)

---

## Phase 4 — Testing (open)

`Messenger.Tests` scaffolded (xUnit v3 + FluentAssertions + Testcontainers, in
`Messenger.slnx`). 31 tests, all passing (25 unit + 6 integration).

- [x] Unit tests: `RoutingSmsSender` + `SmsOptions` (10 tests, all passing)
  - [x] Fallback behaviour — unmatched prefix throws `SmsException` when
        `Sms:AllowConsoleFallback` is false; routes to console when true
        (`RoutingSmsSenderTests`)
  - [x] Prefix grouping — mixed-prefix batch is split per provider; Corvass receives
        only its `+90` recipient (`RoutingSmsSenderTests`). Note: the `+1`/Twilio
        branch can't be exercised in a unit test under the concrete-type design
        ([Infrastructure LESSONS](Messenger.Infrastructure/LESSONS.md) #2) — the test
        asserts the Corvass-side split and stops before the Twilio dispatch.
  - [x] Retry exhaustion — `SmsException` (with provider context) after
        `RetryCount + 1` attempts (`RoutingSmsSenderTests`). This test caught a bug:
        the old catch filter `when (attempt < totalAttempts)` let the final attempt's
        raw exception escape unwrapped, making `throw new SmsException` dead code
        ([Infrastructure LESSONS](Messenger.Infrastructure/LESSONS.md) #4).
  - [x] Unknown provider name (prefix maps to an unregistered sender) throws
        `SmsException`, even with console fallback enabled (`RoutingSmsSenderTests`)
  - [x] `SmsOptions.ResolveProvider` — matching, no-match → null, empty map → null,
        and longest-prefix-first (`+1204` beats `+1`) (`SmsOptionsTests`)
- [x] Integration tests: Testcontainers Postgres + migration fixture (platform pattern),
      `MessageLogs` write-on-success and write-on-failure paths (`PostgresFixture`,
      `MessagesApiTests`). `POST /api/messages/sms` is driven end-to-end against a real
      `postgres:16-alpine` container with the SMS provider swapped for a fake ISmsSender;
      asserts 200 + `Sent` log on success and 500 + `Failed` log on send failure.
      Required `public partial class Program` in `Program.cs`. The pre-existing
      Npgsql/EFCore version drift (CS1705) is now fixed at the source — see Phase 3.
- [x] Provider senders behind fakes — verify request shapes for Corvass/WABA/FCM
      (`ProviderSenderTests`). A capturing `HttpMessageHandler` asserts each sender's
      method, URL, auth header, and JSON body: Corvass (auth in body), WhatsApp
      (`Bearer` token + `messaging_product` payload), FCM HTTP v1 (`Bearer` OAuth token +
      `message:{token,notification}` envelope), plus a fail-fast test when FCM is
      unconfigured. Twilio is excluded — it sends via the SDK global client.
- [x] FCM push hardening tests (see Phase 3 entry). `ProviderSenderTests`: error
      classification from real v1 error payloads — 404/UNREGISTERED and 400/INVALID_ARGUMENT
      → `InvalidToken`, 429/QUOTA_EXCEEDED → `QuotaExceeded` (+`RetryAfter` capture),
      500/INTERNAL and 403/SENDER_ID_MISMATCH → `ProviderError`, non-JSON body doesn't crash
      the parser. `FcmResilienceTests`: builds the production pipeline via
      `HttpPolicies.ConfigureFcmResilience` — 429 retried honoring `Retry-After: 0` (3 attempts
      then success, fast because the header overrides exponential backoff), 400/404 never
      retried, exhausted retries surface the final 429 after 4 attempts. `MessagesApiTests`:
      `POST /api/messages/push` end-to-end against Postgres — 200/`Sent`, 410 + `ErrorCode`
      `UNREGISTERED` logged, 429 + `Retry-After` header, 502; fake `IPushNotificationSender`
      throwing `PushSendException`.

---

## Phase 5 — Candidate features (not committed)

- [ ] Auth.Api OTP SMS routed through ULAK (consolidates all SMS dispatch; requires
      Phase 2 auth first — see AuthenticationIdentity ROADMAP Phase 6)
- [ ] Delivery status callbacks/webhooks from providers → update `MessageLogs.Status`
      beyond Sent/Failed
- [ ] `GET /api/health/log-gap` operational endpoint (recent `MessageLog` write failures)
- [ ] Additional SMS providers (e.g. Netgsm as secondary Turkish route)
