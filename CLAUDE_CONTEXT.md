# ULAK Messenger — Full Architecture & Project Context
> Upload this file to your claude.ai ULAK Project as a Project File.
> For Claude Code repo context, see CLAUDE.md in solution root.

---

## 1. Purpose

**ULAK** (codename: Messenger) is a **pure transport gateway** for multi-channel messaging.
It is one of two foundational base platforms (alongside Auth) that all business applications
consume exclusively via HTTP. It has **no business logic** — it never generates OTPs, manages
contacts, runs campaigns, or renders templates. It only sends what it is told to send.

**Production URL:** `https://ulak.akgyh.com` (host port 5002 → container 8080)

Supported channels:
- **SMS** — Corvass (Turkey `+90`), Twilio (US/CA `+1`)
- **Email** — SMTP via MailKit
- **WhatsApp** — WhatsApp Business API (WABA)
- **Push** — Firebase Cloud Messaging (FCM)

---

## 2. Technology stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 ASP.NET Core Web API |
| ORM | EF Core 10 + Npgsql (PostgreSQL) — message log only |
| Email | MailKit (SMTP) |
| SMS Turkey | Corvass HTTP API |
| SMS US/CA | Twilio SDK |
| WhatsApp | WhatsApp Business API |
| Push | Firebase Cloud Messaging |
| Resilience | Polly (retry + timeout on HttpClient) |
| Deployment | Docker (linux-x64) behind Nginx on VPS |
| IDE | Visual Studio 2026 Community v18.4.2, `.slnx` format |

---

## 3. Solution structure

```
Messenger.slnx
├── Messenger.Core/
│   ├── DTOs/              — SmsMessage, EmailMessage, WhatsAppMessage, PushMessage
│   ├── Interfaces/        — ISmsSender, IEmailSender, IWhatsAppMessageSender, IPushNotificationSender
│   ├── Options/           — EmailOptions, WhatsAppOptions, FcmNotificationOptions
│   └── MessengerService.cs — Facade: routes calls to the correct channel sender
│
├── Messenger.Infrastructure/
│   ├── Senders/
│   │   ├── CorvassSmsSender.cs    — SMS via Corvass API (Turkey)
│   │   ├── TwilioSmsSender.cs     — SMS via Twilio (US/CA)
│   │   ├── RoutingSmsSender.cs    — Routes by E.164 prefix to correct provider
│   │   ├── ConsoleSmsService.cs   — Dev fallback — logs to console, never use in prod
│   │   ├── SmtpEmailSender.cs     — Email via MailKit
│   │   ├── WhatsAppSender.cs      — WhatsApp via WABA
│   │   └── FcmPushSender.cs       — Push via FCM
│   ├── Data/
│   │   └── MessengerDbContext.cs   — EF Core — MessageLog table only
│   └── Config/
│       ├── MessengerInfrastructureModule.cs — All DI registrations for Infrastructure
│       └── HttpPolicies.cs                  — Polly retry + timeout policies
│
└── Messenger.Api/
    ├── Controllers/
    │   └── MessagesController.cs  — POST /api/messages/{sms|email|whatsapp|push}
    ├── Middleware/
    │   └── CorrelationIdMiddleware.cs
    └── Program.cs
```

---

## 4. API endpoints

| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/messages/sms` | Send SMS |
| POST | `/api/messages/email` | Send Email |
| POST | `/api/messages/whatsapp` | Send WhatsApp message |
| POST | `/api/messages/push` | Send push notification |

**Security note:** The API is currently unauthenticated. All callers must be trusted
internal services on the same VPS network. Adding an `X-Api-Key` header check is an
open roadmap item — do not expose this API publicly without it.

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

## 5. SMS routing

`RoutingSmsSender` selects a provider based on the E.164 prefix map in `Sms:ProviderPrefixes`:

```
+90  → CorvassSmsSender   (Turkey — Corvass HTTP API)
+1   → TwilioSmsSender    (US/CA — Twilio SDK)
```

Routing is **config-driven** — never hardcode provider selection. Adding a new country
means adding a prefix entry in config and registering the corresponding sender. The
`RoutingSmsSender` holds concrete sender references (not the interface) — acceptable for
the current fixed provider set, but use keyed registration if a third SMS provider is added.

---

## 6. Hard architectural rules

1. **Pure transport only.** Never add OTP lifecycle logic, campaign logic, contact
   management, or template rendering. Those belong in Auth and Campaigns respectively.
2. **No shared assemblies between platforms.** Callers use HTTP only — no shared NuGet
   packages or project references to ULAK from consuming solutions.
3. **Options pattern always.** Never inject `IConfiguration` directly. Use `IOptions<T>`
   bound in `MessengerInfrastructureModule` (channel options) or `Program.cs` (routing).
4. **DI modules encapsulate registration.** `MessengerInfrastructureModule` owns all
   Infrastructure-layer registrations. `Program.cs` stays thin.
5. **`MessageLog` write is fire-and-forget.** A logging failure must never fail delivery.
   The `catch` in `WriteLogAsync` swallows the exception and only logs the error — this is
   intentional. Ensure the `LogError` call is wired to a structured sink (Seq, etc.) so
   missing rows surface as alerts rather than silent data loss.

---

## 7. Configuration

```json
{
  "ConnectionStrings": {
    "UlakConnection": "Host=...;Database=...;Username=...;Password=..."
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
  },
  "Messaging": {
    "Email": { "SmtpHost": "", "SmtpPort": 587, "SenderEmail": "", "SenderPassword": "" },
    "Whatsapp": { "ApiUrl": "", "ApiKey": "", "SenderNumber": "" },
    "FcmNotification": { "ServerKey": "", "FcmEndpoint": "" }
  }
}
```

Never commit real secrets. All production secrets live in `~/apps/.env` on the VPS.

---

## 8. Deployment

**Production URL:** `https://ulak.akgyh.com`  
**VPS:** `root@187.124.233.239`

**Three-step deploy:**

```bash
# 1. Publish
dotnet publish Messenger.Api/Messenger.Api.csproj -c Release -r linux-x64 --self-contained false -o ./publish

# 2. Upload
scp -O -r /c/Users/sarpa/source/repos/10/Messenger/publish/ root@187.124.233.239:~/apps/ulak-messenger/publish_new/
ssh root@187.124.233.239 "cp -r ~/apps/ulak-messenger/publish_new/. ~/apps/ulak-messenger/publish/ && rm -rf ~/apps/ulak-messenger/publish_new"

# 3. Rebuild and restart
ssh root@187.124.233.239 "cd ~/apps && docker compose build ulak-messenger && docker compose up -d ulak-messenger"
```

**Deploy files in this repo (`deploy/`):**

| File | Mirrors on VPS |
|---|---|
| `deploy/Dockerfile` | `~/apps/ulak-messenger/Dockerfile` — update both together |
| `deploy/docker-compose.yml` | Standalone dev compose (prod uses platform compose) |
| `deploy/nginx/ulak.conf` | `ulak.akgyh.com` block in `/etc/nginx/sites-enabled/apis.conf` |

**VPS directory layout:**
```
~/apps/
├── docker-compose.yml       # platform compose
├── .env                     # all secrets — never committed
└── ulak-messenger/
    ├── Dockerfile
    └── publish/             # dotnet publish output
```

| Container | Domain | Host port | Container port |
|---|---|---|---|
| `ulak-service` | `https://ulak.akgyh.com` | 5002 | 8080 |

PostgreSQL runs natively on VPS; containers reach it via `host.docker.internal`.

---

## 9. Known gotchas

- **Namespace copy-paste:** `Messenger.Core` options classes must use `namespace Messenger.Core.Models;`
  not `namespace AuthApi.Models;`. Always update the namespace when copying a file between projects.
- **`TwilioClient.Init()` is global state.** Calling it in a constructor on every scope
  request is safe with a single credential set, but hazardous if multi-account support is
  ever added. Call it once at startup in `Program.cs` instead.
- **EF Core + Npgsql versions must match.** All packages are pinned in `Directory.Packages.props`.
  Do not upgrade individual packages — upgrade all together.
- **`publish/` is gitignored.** Never run `git add -A` near a deploy cycle without checking
  `git status` first.

---

## 10. Coding conventions

- .NET 10 / C# 13: primary constructors, collection expressions, top-level namespaces
- One concern per class — if a class is growing business logic, it's in the wrong project
- `CancellationToken` on all async methods
- Always read a file before editing — never edit from assumptions
- `#nullable enable` across all projects
- Build and verify with `dotnet build` after every change
