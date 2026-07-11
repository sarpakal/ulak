# CLAUDE.md — Messenger.Api

HTTP entry point of ULAK. Keep it thin: controllers delegate to `MessengerService`;
all provider logic lives in `Messenger.Infrastructure`.

> Failure log: [LESSONS.md](LESSONS.md) · solution rules: [../CLAUDE.md](../CLAUDE.md)

---

## Hard rules

1. **No business logic.** No templates, no OTP, no scheduling, no contact lookups —
   callers send fully-composed text.
2. **No new provider code here.** Senders belong in `Messenger.Infrastructure`,
   registered via `MessengerInfrastructureModule`.
3. **Options pattern only.** Never inject `IConfiguration` into a controller or service.
4. **`MessageLogs` write stays best-effort.** `WriteLogAsync` swallows exceptions by
   design — a logging failure must never fail a delivery response. Do not rethrow.
5. **`CorrelationIdMiddleware` stays first** in the pipeline (before anything that logs).
6. **`UsePathBase("/messenger")` stays** — the VPS-internal route depends on it.

---

## Known design debt (do not extend, fix per roadmap)

- `MessagesController` injects `MessengerDbContext` directly — Infrastructure code in the
  Api layer. Planned fix: extract `IMessageLogService` ([LESSONS.md](LESSONS.md) #2,
  [ROADMAP](../ROADMAP.md) Phase 3). Don't add more EF usage to controllers meanwhile.
- **No authentication on any endpoint** — adding any new endpoint makes the unauthenticated
  surface bigger. Check [SECURITY.md](../SECURITY.md) first; auth is Phase 2 of the roadmap.

---

## Config read in `Program.cs`

| Key | Purpose |
|-----|---------|
| `ConnectionStrings:UlakConnection` | Postgres — **fails fast if missing** |
| `Messaging:Email` / `Messaging:Whatsapp` / `Messaging:FcmNotification` | Channel options (live) |

Logging is JSON console with scopes — correlation ids appear in `docker logs ulak-service`.

---

## Adding an endpoint checklist

1. DTO in `Messenger.Core/DTOs/` (record), interface method if a new channel
2. Sender in `Messenger.Infrastructure/Senders/`, registered in `MessengerInfrastructureModule`
3. Facade method on `MessengerService`
4. Controller action: log → try/send → `finally WriteLogAsync` → `Ok`/throw (match existing shape)
5. Consider rate limiting and auth — neither is applied globally (see [SECURITY.md](../SECURITY.md))
