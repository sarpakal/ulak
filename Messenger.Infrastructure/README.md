# Messenger.Infrastructure

Provider implementations for ULAK: SMS (Corvass, Twilio, routing), Email (SMTP/MailKit),
WhatsApp (WABA), Push (FCM), plus EF Core persistence and the DI module.

---

## Contents

```
Messenger.Infrastructure/
├── Senders/
│   ├── RoutingSmsSender.cs    — the registered ISmsSender; groups recipients by E.164
│   │                            prefix, dispatches per provider, app-level retry,
│   │                            throws SmsException after exhausted retries
│   ├── CorvassSmsSender.cs    — Turkey (+90); typed HttpClient + Polly
│   ├── TwilioSmsSender.cs     — US/CA (+1); stub pending Twilio NuGet activation
│   ├── ConsoleSmsService.cs   — dev fallback (logs instead of sending) — see SECURITY
│   ├── SmtpEmailSender.cs     — MailKit
│   ├── WhatsappSender.cs      — WhatsApp Business API; typed HttpClient + Polly
│   └── FcmPushSender.cs       — Firebase Cloud Messaging; typed HttpClient + Polly
├── Data/MessengerDbContext.cs — MessageLogs table (single-entity model)
├── Migrations/                — EF Core migrations (applied on startup by the Api)
└── Config/
    ├── MessengerInfrastructureModule.cs — ALL DI registrations for this layer
    └── HttpPolicies.cs                  — shared Polly retry + timeout policies
```

---

## Rules

- Every registration goes through `MessengerInfrastructureModule.AddMessengerInfrastructure()` —
  never register Infrastructure types in `Program.cs`.
- Outbound HttpClients get `HttpPolicies.GetRetryPolicy()` + `GetTimeoutPolicy()` and a
  5-minute handler lifetime.
- SMS provider selection comes from `Sms:ProviderPrefixes` config via
  `SmsOptions.ResolveProvider` — never hardcode.
- EF Core + Npgsql package versions move together (`10.0.x`) — never upgrade one alone.

---

## Docs

Solution-level: [README](../README.md) · [ARCHITECTURE](../ARCHITECTURE.md) ·
[SECURITY](../SECURITY.md) · [ROADMAP](../ROADMAP.md) · failure log: [LESSONS.md](LESSONS.md)
