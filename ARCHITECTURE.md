# ULAK Messenger — Architecture

End-to-end design of the messaging gateway: project structure, send flow, SMS routing,
persistence, resilience, and deployment topology.

Related: [SECURITY.md](SECURITY.md) · [ROADMAP.md](ROADMAP.md) · [LESSONS.md](LESSONS.md) ·
platform-wide [`../ARCHITECTURE.md`](../ARCHITECTURE.md)

---

## Role in the platform

ULAK is one of the two base platform services (alongside AuthenticationIdentity). It is a
**pure transport gateway**: consuming solutions (Campaigns, SMSGuardian, n8n workflows) POST
fully-composed messages and ULAK routes them to the right external provider. It contains no
templates, no scheduling, no OTP logic, no contact management.

> Note: Auth.Api currently embeds its own Corvass/Twilio senders rather than calling ULAK —
> consolidating OTP SMS dispatch through ULAK is a roadmap candidate in both solutions.

---

## Solution Structure

```
Messenger.slnx
├── Messenger.Core            — contracts: DTOs, interfaces, options. No external dependencies.
├── Messenger.Infrastructure  — provider implementations + EF Core + DI module
└── Messenger.Api             — HTTP entry point (controllers + middleware)
```

### Dependency direction

```
Messenger.Api ──► Messenger.Infrastructure ──► Messenger.Core
Messenger.Api ──► Messenger.Core
```

`Messenger.Core` references nothing. Infrastructure implements Core's interfaces.
The Api layer should depend on Core abstractions only — the current direct
`MessengerDbContext` injection in `MessagesController` is a known violation
([LESSONS](Messenger.Api/LESSONS.md)).

---

## Send Flow

```
Caller                      Messenger.Api                        Provider
  │  POST /api/messages/sms     │
  │ ───────────────────────────►│  CorrelationIdMiddleware (early — id into log scope)
  │                             │  MessagesController.SendSms
  │                             │    └─ MessengerService.SendSmsAsync (Core facade)
  │                             │         └─ ISmsSender = RoutingSmsSender
  │                             │              ├─ group recipients by E.164 prefix
  │                             │              ├─ +90 group → CorvassSmsSender ──► Corvass HTTP API
  │                             │              ├─ +1  group → TwilioSmsSender  ──► Twilio
  │                             │              └─ no match  → ConsoleSmsService (dev fallback — see SECURITY)
  │                             │  finally: WriteLogAsync → MessageLogs row (best-effort)
  │◄────────────────────────────│  200 { "status": "SMS sent" }  /  500 on provider failure
```

Email (`SmtpEmailSender` via MailKit), WhatsApp (`WhatsappSender` via WABA HTTP), and Push
(`FcmPushSender` via FCM) follow the same controller → facade → sender shape without routing.

### SMS routing and retry

- `SmsOptions.ResolveProvider(number)` maps the E.164 prefix via `Sms:ProviderPrefixes`
  config (`+90` → `"Corvass"`, `+1` → `"Twilio"`). Never hardcode provider selection.
- A mixed-prefix recipient list is grouped and dispatched per provider.
- `SendWithRetryAsync`: `Sms:RetryCount` retries (default 1) with `Sms:RetryDelayMs` delay;
  exhausted retries throw `SmsException` (recipient + provider attached) → HTTP 500.
- Unmatched prefixes currently fall back to `ConsoleSmsService` — a dev safety net that is a
  production hazard ([LESSONS](Messenger.Infrastructure/LESSONS.md)).

---

## Persistence — MessageLog

Single-table EF Core model (`MessengerDbContext`):

```
MessageLogs
  Id (identity int), Channel, Recipient, Payload?, Status ("Sent"|"Failed"),
  CorrelationId?, CreatedAt (timestamptz)
```

- Written in a `finally` block after every send — **best-effort**: a DB failure is logged
  and swallowed, never failing the delivery response.
- `Payload` stores the message text/subject/title in plaintext — see
  [SECURITY.md](SECURITY.md) → Data at rest.
- Migrations applied on startup via `Database.Migrate()` in `Program.cs`
  (connection string: `ConnectionStrings:UlakConnection`).

---

## Correlation IDs

`CorrelationIdMiddleware` runs first in the pipeline so every log line and the
`MessageLogs.CorrelationId` column carry the request's correlation id. Logging is
JSON-console (`AddJsonConsole`, `IncludeScopes = true`) so the id appears in `docker logs`.

---

## DI and Resilience

All Infrastructure registrations live in `MessengerInfrastructureModule.AddMessengerInfrastructure()`;
`Program.cs` stays thin.

| Sender | Registration | Resilience |
|--------|-------------|-----------|
| `CorvassSmsSender` | `AddHttpClient<T>` (typed, concrete) | Polly retry + timeout, 5 min handler lifetime |
| `TwilioSmsSender` | `AddScoped` (SDK manages HTTP) | SDK-internal |
| `WhatsappSender` | `AddHttpClient<IWhatsAppMessageSender, T>` | Polly retry + timeout |
| `FcmPushSender` | `AddHttpClient<IPushNotificationSender, T>` | Polly retry + timeout |
| `SmtpEmailSender` | `AddScoped<IEmailSender, T>` | MailKit |
| `RoutingSmsSender` | `AddScoped<ISmsSender, T>` | App-level retry loop (above) |

Polly policies are defined once in `HttpPolicies` (retry + timeout).
Known design debt: `RoutingSmsSender` injects the concrete sender types, and
`TwilioClient.Init()` runs in the sender constructor — both documented with fixes in
[Messenger.Infrastructure/LESSONS.md](Messenger.Infrastructure/LESSONS.md).

---

## Configuration Reference

| Key | Bound to | Notes |
|-----|----------|-------|
| `ConnectionStrings:UlakConnection` | `Program.cs` | Postgres; **fails fast if missing** |
| `Corvass:*` | `CorvassOptions` (`MessengerInfrastructureModule`) | Live Corvass binding — SmsUrl, ApiKey, ApiSecret, Originator, MessageType, RecipientType |
| `Twilio:*` | `TwilioOptions` | AccountSid, AuthToken, FromNumber |
| `Sms:*` | `SmsOptions` | RetryCount, RetryDelayMs, ProviderPrefixes |
| `Messaging:Email:*` | `EmailOptions` (`Program.cs`) | SMTP host/port/sender |
| `Messaging:Whatsapp:*` | `WhatsAppOptions` | WABA URL, key, sender number |
| `Messaging:FcmNotification:*` | `FcmNotificationOptions` | FCM HTTP v1 project id + service-account credentials path |

Never inject `IConfiguration` into services — options pattern only.

---

## Deployment

```
Internet
   │
   ▼
Nginx (VPS: 187.124.233.239, TLS via Certbot)
   ├── ulak.akgyh.com            ──► ulak-service container (host 5002 → container 8080)
   └── http://<vps-ip>/messenger/ ──► same container (VPS-internal prefix route;
                                      app calls UsePathBase("/messenger"))

PostgreSQL — native on VPS host (host.docker.internal)
   └── MessengerDb ◄── ulak-service (migrations on startup)
```

Secrets come from `~/apps/.env` via Docker Compose — never committed.

`deploy/` in this repo mirrors the live VPS files (update both together, then redeploy):

| Repo path | VPS path |
|-----------|----------|
| `deploy/Dockerfile` | `~/apps/ulak-messenger/Dockerfile` |
| `deploy/nginx/ulak.conf` | ulak section of `/etc/nginx/sites-enabled/apis.conf` (reference only — never push the per-solution file directly) |
| `deploy/docker-compose.yml` | standalone dev compose; production uses the platform compose |

Three-step deploy (publish → scp staging pattern → compose rebuild): see
[CLAUDE.md](CLAUDE.md) → Deployment, or run the platform `Deploy.cmd` pattern.
