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
│   │   ├── OtpService.cs            # ⚠️ TO BE REMOVED — belongs in Auth Platform
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
    "Messenger": "Host=...;Database=...;Username=...;Password=..."
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
| IDE          | Visual Studio 2022/2026                 |
| OS           | Windows 11                              |

