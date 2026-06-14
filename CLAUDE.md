# CLAUDE.md — ULAK Messenger Platform

## Purpose
This file guides Claude Code when working in this repository.
Read it before making any changes.

---

## What This Project Is

**ULAK** (codename: Messenger) is a **pure transport gateway** for multi-channel messaging.
It is one of two foundational base platforms (alongside Auth Platform) that business
applications consume exclusively via HTTP. It has no business logic of its own.

Supported channels:
- SMS (Corvass for Turkey +90, Twilio CA for Canada +1, Twilio USA for US +1)
- Email (SMTP via MailKit)
- WhatsApp (WhatsApp Business API)
- Push Notifications (Firebase Cloud Messaging)

### Documentation map

| Document | Content |
|----------|---------|
| [CLAUDE_CONTEXT.md](CLAUDE_CONTEXT.md) | Full AI context: architecture, SMS routing, hard rules, deployment (upload to claude.ai Project) |
| [README.md](README.md) | Setup, run, project index |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Send flow, SMS routing, persistence, DI, deployment topology |
| [SECURITY.md](SECURITY.md) | Threat model — **note: the API is currently unauthenticated** |
| [ROADMAP.md](ROADMAP.md) | Done phases + open security/debt/testing items |
| [LESSONS.md](LESSONS.md) | Cross-project failure log (per-project logs in each project) |

Per-project guidance: [Messenger.Api/CLAUDE.md](Messenger.Api/CLAUDE.md) ·
[Messenger.Core/CLAUDE.md](Messenger.Core/CLAUDE.md) ·
[Messenger.Infrastructure/CLAUDE.md](Messenger.Infrastructure/CLAUDE.md)

---

## Hard Architectural Rules

1. **ULAK is a transport layer only.** It must never contain OTP lifecycle logic,
   campaign logic, contact/lead management, or template rendering. Those belong
   in Auth Platform and Campaigns App respectively.

2. **No shared code between platforms.** Auth, Campaigns, and other business apps
   call ULAK via HTTP only — never via shared assemblies or NuGet packages.

3. **SMS routing is country-specific** due to regulatory requirements:
   - `+90` → Corvass (Turkey)
   - `+1` CA → Twilio Canada
   - `+1` US → Twilio USA
   Routing is configured in `appsettings.json` under `Sms:ProviderPrefixes`.
   Routing logic lives in `RoutingSmsSender`. Never hardcode provider selection.

4. **OTP belongs in Auth.** ULAK exposes `/api/messages/sms` which Auth calls
   with an already-composed message text. ULAK never generates, stores, or
   verifies OTP codes.

5. **Options pattern always.** Never inject `IConfiguration` directly into
   services. Use `IOptions<T>` bound in `MessengerInfrastructureModule`
   (channel options) or `Program.cs` (routing/infra options).

6. **DI modules encapsulate registration.** `MessengerInfrastructureModule`
   owns all Infrastructure-layer registrations. `Program.cs` should stay thin.

---

## Solution Structure

```
Messenger.slnx
├── Messenger.Core/                  # Contracts only — no external dependencies
│   ├── DTOs/                        # SmsMessage, EmailMessage, WhatsAppMessage, PushMessage
│   ├── Interfaces/                  # ISmsSender, IEmailSender, IWhatsAppMessageSender, IPushNotificationSender
│   ├── Options/                     # EmailOptions, CorvassApiOptions, WhatsAppOptions, FcmNotificationOptions, OtpOptions
│   └── MessengerService.cs          # Facade — routes calls to the correct sender
│
├── Messenger.Infrastructure/        # Provider implementations
│   ├── Senders/
│   │   ├── CorvassSmsSender.cs      # SMS via Corvass API (Turkey)
│   │   ├── TwilioSmsSender.cs       # SMS via Twilio (US/CA) — stub, needs Twilio NuGet
│   │   ├── RoutingSmsSender.cs      # Routes by E.164 prefix to correct provider
│   │   ├── ConsoleSmsService.cs     # Dev fallback — logs to console, never use in prod
│   │   ├── SmtpEmailSender.cs       # Email via MailKit
│   │   ├── WhatsAppSender.cs        # WhatsApp via WABA
│   │   ├── FcmPushSender.cs         # Push via FCM
│   │   └── ISmsService.cs           # Internal routing abstraction
│   ├── Data/
│   │   └── MessengerDbContext.cs     # EF Core — MessageLog only
│   └── Config/
│       ├── MessengerInfrastructureModule.cs   # All DI registrations
│       └── HttpPolicies.cs                    # Polly retry + timeout policies
│
└── Messenger.Api/                   # ASP.NET Core Web API
    ├── Controllers/
    │   └── MessagesController.cs    # POST /api/messages/{sms|email|whatsapp|push}
    ├── Middleware/
    │   └── CorrelationIdMiddleware.cs
    └── Program.cs
```

---

## API Endpoints

| Method | Endpoint                  | Description             |
|--------|---------------------------|-------------------------|
| POST   | `/api/messages/sms`       | Send SMS                |
| POST   | `/api/messages/email`     | Send Email              |
| POST   | `/api/messages/whatsapp`  | Send WhatsApp           |
| POST   | `/api/messages/push`      | Send Push Notification  |

### Request shapes

```json
// SMS
{ "to": ["+905551234567"], "text": "Your message" }

// Email
{ "to": ["user@example.com"], "subject": "Subject", "body": "Body text", "cc": [], "bcc": [] }

// WhatsApp
{ "to": "+905551234567", "text": "Your message" }

// Push
{ "to": "device-fcm-token", "title": "Title", "body": "Body" }
```

---

## Configuration Structure (appsettings.json)

```json
{
  "ConnectionStrings": {
    "UlakConnection": "Host=...;Database=...;Username=...;Password=..."
  },
  "Messaging": {
    "Email": { "SmtpHost": "", "SmtpPort": 587, "SenderEmail": "", "SenderPassword": "" },
    "CorvassApi": { "SmsUrl": "", "ApiKey": "", "ApiSecret": "" },
    "Whatsapp": { "ApiUrl": "", "ApiKey": "", "SenderNumber": "" },
    "FcmNotification": { "ServerKey": "", "FcmEndpoint": "" }
  },
  "Corvass": {
    "SmsUrl": "", "ApiKey": "", "ApiSecret": "",
    "Originator": "AKAL YNT.", "MessageType": "B", "RecipientType": "BIREYSEL"
  },
  "Twilio": {
    "AccountSid": "", "AuthToken": "", "FromNumber": ""
  },
  "Sms": {
    "RetryCount": 1,
    "RetryDelayMs": 1000,
    "ProviderPrefixes": { "+90": "Corvass", "+1": "Twilio" }
  }
}
```

Never commit real secrets. Use `dotnet user-secrets` in development.

---

## Development Principles

- **Targeted additions only.** Never rewrite a file unless explicitly asked.
  Read the actual file first, then make the minimum change needed.
- **Always read before editing.** Use the actual file contents, not assumptions.
- **One concern per class.** If a class is growing business logic, it's wrong.
- **Build after changes.** Run `dotnet build` to verify changes compile before finishing.
- **EF Core + Npgsql versions must match.** All packages pinned to `10.0.5` /
  `10.0.1` (Npgsql). Do not upgrade individual packages — upgrade all together.

---

## Tech Stack

| Layer        | Technology                              |
|--------------|-----------------------------------------|
| Runtime      | .NET 10, ASP.NET Core Web API           |
| Database     | PostgreSQL via EF Core + Npgsql 10.0.x  |
| Email        | MailKit                                 |
| SMS Turkey   | Corvass HTTP API                        |
| SMS US/CA    | Twilio (NuGet stub — not yet activated) |
| WhatsApp     | WhatsApp Business API (WABA)            |
| Push         | Firebase Cloud Messaging (FCM)          |
| Resilience   | Polly (retry + timeout on HttpClient)   |
| IDE          | Visual Studio 2026                      |
| OS           | Windows 11                              |

---

## Deployment Environment

- **Production runtime**: Docker container on Linux VPS (Ubuntu)
- **Production URL**: `https://ulak.akgyh.com`
- **Database**: PostgreSQL (native on VPS, not containerized)
- **Reverse proxy**: Nginx with SSL (Certbot / Let's Encrypt, auto-renewing)
- **Automation**: n8n (Docker) calls ULAK via HTTP for workflows

### Deploy files in this repo (`deploy/`)

| File | Purpose |
|------|---------|
| `deploy/Dockerfile` | Matches `~/apps/ulak-messenger/Dockerfile` on VPS. Update both together. |
| `deploy/docker-compose.yml` | Standalone dev compose. Production uses the platform `deploy/docker-compose.yml`. |
| `deploy/nginx/ulak.conf` | The `ulak.akgyh.com` server block. Mirrors the ulak section of `/etc/nginx/sites-enabled/apis.conf` on VPS. |

### Directory structure on VPS
```
~/apps/
├── docker-compose.yml            # platform compose — source at C:\repos\10\deploy\docker-compose.yml
├── .env                          # all secrets, never committed to git
├── ulak-messenger/
│   ├── Dockerfile                # mirrors deploy/Dockerfile in this repo
│   └── publish/                  # dotnet publish linux-x64 output
└── n8n/
    └── data/                     # n8n persistent volume
```

### Container map

| Container | Domain | Host port | Container port |
|-----------|--------|-----------|----------------|
| `auth-service` | `https://auth.akgyh.com` | 5001 | 8080 |
| `ulak-service` | `https://ulak.akgyh.com` | 5002 | 8080 |
| `ingest-service` | `https://ingest.akgyh.com` | 5010 | 8080 |

- All secrets come from `~/apps/.env` via env_file — never committed.
- PostgreSQL runs natively on the VPS host; containers reach it via `host.docker.internal`.

### Deployment steps
1. Publish on Windows:
   ```
   dotnet publish Messenger.Api/Messenger.Api.csproj -c Release -r linux-x64 --self-contained false -o ./publish
   ```
2. Upload to VPS:
   ```
   scp -O -r /c/Users/sarpa/source/repos/10/Messenger/publish/ root@187.124.233.239:~/apps/ulak-messenger/publish_new/
   ssh root@187.124.233.239 "cp -r ~/apps/ulak-messenger/publish_new/. ~/apps/ulak-messenger/publish/ && rm -rf ~/apps/ulak-messenger/publish_new"
   ```
3. On VPS: `cd ~/apps && docker compose build ulak-messenger && docker compose up -d`
4. If `deploy/Dockerfile` changes: also update `~/apps/ulak-messenger/Dockerfile` and redeploy.
5. If `deploy/nginx/ulak.conf` changes: also update `/etc/nginx/sites-enabled/apis.conf` and run `nginx -s reload`.

### What this means for code changes
- Configuration must support both appsettings.json (dev) and environment variables (prod).
- Never hardcode URLs, ports, or credentials.
- Never change the publish output path (`./publish`) — the Dockerfile's `COPY publish/ .` must stay in sync.

---

## Lessons learned

> Cross-project log: **[LESSONS.md](LESSONS.md)** · per-project logs:
> [Messenger.Api](Messenger.Api/LESSONS.md) · [Messenger.Core](Messenger.Core/LESSONS.md) ·
> [Messenger.Infrastructure](Messenger.Infrastructure/LESSONS.md)

### `AuthApi.Models` namespace on Messenger.Core options was a copy-paste artifact
`CorvassOptions`, `TwilioOptions`, and `SmsOptions` were copied from Auth.Api and their `namespace AuthApi.Models;` declaration was never updated. Every Infrastructure file that consumed them had `using AuthApi.Models;`, making it look like Messenger depended on Auth's types. Fixed: renamed to `namespace Messenger.Core.Models;` across all three files and updated all consumers. Rule: always update the namespace immediately when copying a file into a different project.

### `TwilioClient.Init()` in a constructor re-initialises global SDK state on every request
`TwilioSmsSender` is registered as Scoped. Its constructor calls `TwilioClient.Init(accountSid, authToken)`, which sets the Twilio SDK's global static REST client. This runs once per HTTP request scope — harmless with a single set of credentials but a silent correctness hazard if credentials ever change mid-process or if multiple accounts are needed. The correct pattern is to call `TwilioClient.Init()` once at application startup (in `Program.cs`) or pass a per-call `TwilioRestClient` instance rather than relying on the global default.

### `CorvassSmsSender` is injected as a concrete type into `RoutingSmsSender`
`RoutingSmsSender` holds fields typed as `CorvassSmsSender` and `TwilioSmsSender` rather than `ISmsSender`. This bypasses the interface abstraction and makes swapping or mocking individual providers harder. The DI module also registers the concrete types directly (`services.AddScoped<CorvassSmsSender>()`). Acceptable for now given the fixed provider set, but if a third SMS provider is added, introduce a keyed registration or a named-factory pattern instead of adding another concrete field to `RoutingSmsSender`.

### `MessageLog` write is intentionally fire-and-forget with swallowed exceptions
`MessagesController.WriteLogAsync` wraps the EF Core save in `try/catch` and only logs the error — it never rethrows. This is by design: a logging failure must not fail the message delivery. However, it also means log gaps are invisible without monitoring. Ensure the `LogError` call is connected to a structured logging sink (Seq, Application Insights, etc.) so missing log rows surface as alerts rather than silent data loss.

### `publish/` and `publish_out/` are gitignored — do not use `git add -A` before deploy
Both directories are in `.gitignore`. If you run `git add -A` near a `dotnet publish` step the binary output is excluded automatically. However, `git add .` in older Git versions may behave differently — always verify `git status` shows no unexpected binaries staged before committing around a deploy cycle.