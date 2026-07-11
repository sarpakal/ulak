# Messenger.Core

Contracts library for ULAK — DTOs, interfaces, and options. **No external dependencies**
(no EF Core, no provider SDKs, no ASP.NET Core).

---

## Contents

```
Messenger.Core/
├── DTOs/            — SmsMessage, EmailMessage, WhatsAppMessage, PushMessage (records)
├── Interfaces/      — ISmsSender, IEmailSender, IWhatsAppMessageSender,
│                      IPushNotificationSender, ICorrelationContext
├── Options/         — EmailOptions, WhatsAppOptions, FcmNotificationOptions
├── Models/          — CorvassOptions, TwilioOptions, SmsOptions (live SMS bindings;
│                      SmsOptions.ResolveProvider maps E.164 prefix → provider name)
└── MessengerService.cs — facade: routes Send*Async calls to the injected channel senders
```

`MessengerService` is the single type the Api layer calls; one method per channel,
each delegating to the matching sender interface.

---

## Rules

- This project references **nothing** — adding a package here is almost always wrong.
- DTOs are simple records; validation and HTTP concerns belong to the Api layer.
- A copied file's `namespace` must be updated immediately
  (`AuthApi.Models` artifact — see solution [LESSONS.md](../LESSONS.md) #1).

---

## Docs

Solution-level: [README](../README.md) · [ARCHITECTURE](../ARCHITECTURE.md) ·
[SECURITY](../SECURITY.md) · [ROADMAP](../ROADMAP.md) · failure log: [LESSONS.md](LESSONS.md)
