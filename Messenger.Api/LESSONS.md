# Messenger.Api — Failure Log

Project-specific failures and design hazards. Format per entry: **Symptom → Root Cause → Exact Fix**.
Cross-project and operational entries live in the solution [LESSONS.md](../LESSONS.md).

---

## 1. `MessageLog` write is best-effort — log gaps are invisible without monitoring

**Context:** `MessagesController.WriteLogAsync` saves a `MessageLog` row in a `finally` block
after each send. Exceptions are caught, logged via `ILogger`, and swallowed — the send
response is unaffected.

**Symptom**
A `MessageLog` row is silently missing. The send returned HTTP 200, the calling service
assumed the message was delivered, but when querying `MessageLogs` for reporting there are
gaps. No alert fires; no error is returned to the caller.

**Root Cause**
The swallow-and-log design is correct: a DB write failure must not fail message delivery.
But without monitoring on the `LogError` output, swallowed exceptions produce invisible data
loss. A structured logging sink that is not configured means the `LogError` call also goes
nowhere.

**Exact Fix**
Do not change the swallow behaviour. Instead:
1. Confirm the logging sink is connected (`docker logs ulak-service`) after every deploy.
2. Alert on `MessageLog write failed` events from `MessagesController` — any occurrence
   means a log row is missing.
3. Optionally add a `GET /api/health/log-gap` operational endpoint (roadmap Phase 5).

---

## 2. `MessengerDbContext` injected directly into `MessagesController` bypasses the service layer

**Context:** `MessagesController` constructor-injects `MessengerDbContext` alongside
`MessengerService`; `WriteLogAsync` performs EF Core `Add` + `SaveChangesAsync` directly.

**Symptom**
The controller has two responsibilities: routing requests to senders and persisting the
message log. Any test covering the logging path must wire up a real or in-memory EF context.
A `MessageLog` schema change touches both Infrastructure and the Api layer.

**Root Cause**
`WriteLogAsync` was added directly to the controller as a quick way to add logging without
creating a new service — placing Infrastructure-layer code (EF Core operations) inside the
API layer and inverting the dependency arrow.

**Exact Fix**
Move `WriteLogAsync` into an `IMessageLogService` (interface in `Messenger.Core/Interfaces/`,
implementation in `Messenger.Infrastructure`), registered in `MessengerInfrastructureModule`.
The controller injects only the interface; `MessengerDbContext` leaves the constructor.
Non-breaking refactor — HTTP surface and send behaviour unchanged. Tracked in
[ROADMAP](../ROADMAP.md) Phase 3.
