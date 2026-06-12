# Messenger.Api

ASP.NET Core Web API (.NET 10) — the HTTP entry point of the ULAK messaging gateway.
Receives send requests, delegates to `MessengerService` (Core facade), and writes a
best-effort `MessageLogs` row per send.

---

## Run

```bash
# Migrations run automatically on startup (ConnectionStrings:UlakConnection)
dotnet run --project Messenger.Api
```

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/messages/sms` | Send SMS — `{ "to": ["+90…"], "text": "…" }` |
| POST | `/api/messages/email` | Send email — `{ "to": ["a@b.c"], "subject": "…", "body": "…", "cc": [], "bcc": [] }` |
| POST | `/api/messages/whatsapp` | Send WhatsApp — `{ "to": "+90…", "text": "…" }` |
| POST | `/api/messages/push` | Send push — `{ "to": "<fcm-token>", "title": "…", "body": "…" }` |
| GET | `/` | Liveness — returns `"Messenger is running."` |

> ⚠️ No authentication is currently applied to any endpoint — see solution
> [SECURITY.md](../SECURITY.md) before exposing this API anywhere new.

Responses: `200 { "status": "<Channel> sent" }` on success; `500` when the provider fails
after retries. The `MessageLogs` write happens in a `finally` block — log failures never
affect the HTTP response.

---

## Contents

```
Messenger.Api/
├── Controllers/MessagesController.cs   — four send endpoints + WriteLogAsync + liveness
├── Middleware/CorrelationIdMiddleware.cs — runs first; id flows to logs and MessageLogs
└── Program.cs                          — config binding, DbContext, DI module, Migrate()
```

`Program.cs` calls `UsePathBase("/messenger")` to support the VPS-internal
`http://<vps-ip>/messenger/` route alongside `https://ulak.akgyh.com`.

---

## Docs

Solution-level: [README](../README.md) · [ARCHITECTURE](../ARCHITECTURE.md) ·
[SECURITY](../SECURITY.md) · [ROADMAP](../ROADMAP.md) · failure log: [LESSONS.md](LESSONS.md)
