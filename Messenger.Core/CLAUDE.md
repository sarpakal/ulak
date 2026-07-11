# CLAUDE.md — Messenger.Core

Contracts only. This project must compile with **zero external dependencies**.

> Failure log: [LESSONS.md](LESSONS.md) · solution rules: [../CLAUDE.md](../CLAUDE.md)

---

## Hard rules

1. **No package references.** No EF Core, no provider SDKs, no ASP.NET Core types.
   If a change here needs a package, the code belongs in Infrastructure or Api instead.
2. **Interfaces describe channels, not providers.** `ISmsSender`, not `ICorvassSender` —
   provider specifics live in Infrastructure.
3. **Options classes are plain POCOs** bound by the consuming layer
   (`MessengerInfrastructureModule` or `Program.cs`).
4. **DTOs are records.** `SmsMessage` uses `with` cloning in `RoutingSmsSender` —
   keep them immutable records.
5. **Namespace must match project and folder.** The `AuthApi.Models` copy-paste artifact
   (solution [LESSONS.md](../LESSONS.md) #1) is the cautionary tale: first edit after
   copying any file is its `namespace` declaration.

---

## Config-binding map (who binds what)

| Type (here) | Section | Bound in |
|-------------|---------|----------|
| `CorvassOptions`, `TwilioOptions`, `SmsOptions` (`Models/`) | `Corvass:` / `Twilio:` / `Sms:` | `MessengerInfrastructureModule` — **live** |
| `EmailOptions`, `WhatsAppOptions`, `FcmNotificationOptions` (`Options/`) | `Messaging:*` | `Program.cs` — live |

The `Models/` vs `Options/` folder split is historical (the `Models/` trio arrived from the
Auth copy-paste) — worth consolidating into one folder. (The dead `CorvassApiOptions` /
`Messaging:CorvassApi` section was removed 2026-07-11.)
