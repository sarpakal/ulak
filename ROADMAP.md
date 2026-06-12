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

- [ ] **Add authentication** — the API is currently publicly callable with no credentials.
      Decide: nginx IP allowlist (cheapest), static API key middleware, or Auth.Api JWT
      validation per calling service. At minimum, ship the nginx allowlist now.
- [ ] Rate limiting on `/api/messages/*` (pattern: Auth.Api's `auth-by-ip` policy)
- [ ] Throw on unmatched SMS prefix in production instead of `ConsoleSmsService` fallback
      (silent message loss with HTTP 200 — [Infrastructure LESSONS](Messenger.Infrastructure/LESSONS.md))
- [ ] `MessageLogs` retention job (pattern: Auth.Api `AuditRetentionJob`) — payloads are PII
- [ ] Remove dead `Messaging:CorvassApi` config section everywhere (live binding is `Corvass:`)

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

---

## Phase 4 — Testing (open)

No test projects exist yet.

- [ ] Unit tests: `RoutingSmsSender` (prefix grouping, retry exhaustion, fallback behaviour),
      `SmsOptions.ResolveProvider`
- [ ] Integration tests: Testcontainers Postgres + migration fixture (platform pattern),
      `MessageLogs` write-on-success and write-on-failure paths
- [ ] Provider senders behind fakes — verify request shapes for Corvass/WABA/FCM

---

## Phase 5 — Candidate features (not committed)

- [ ] Auth.Api OTP SMS routed through ULAK (consolidates all SMS dispatch; requires
      Phase 2 auth first — see AuthenticationIdentity ROADMAP Phase 6)
- [ ] Delivery status callbacks/webhooks from providers → update `MessageLogs.Status`
      beyond Sent/Failed
- [ ] `GET /api/health/log-gap` operational endpoint (recent `MessageLog` write failures)
- [ ] Additional SMS providers (e.g. Netgsm as secondary Turkish route)
