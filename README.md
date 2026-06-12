# ULAK — Multi-Channel Messaging Gateway

**ULAK** (project name: Messenger) is a pure transport gateway for sending SMS, Email, WhatsApp, and Push Notifications. It exposes a single HTTP API consumed by other backend services (e.g. Auth Platform, Campaigns App). It has no business logic of its own.

---

## Projects

| Project | Type | Role |
|---------|------|------|
| [`Messenger.Api`](Messenger.Api/README.md) | ASP.NET Core Web API (.NET 10) | HTTP entry point — receives send requests, delegates to infrastructure |
| [`Messenger.Core`](Messenger.Core/README.md) | Class library | Contracts only — interfaces, DTOs, options (no external dependencies) |
| [`Messenger.Infrastructure`](Messenger.Infrastructure/README.md) | Class library | Provider implementations — Corvass, Twilio, SMTP, WhatsApp, FCM |

**Documentation:**
[ARCHITECTURE](ARCHITECTURE.md) · [SECURITY](SECURITY.md) · [ROADMAP](ROADMAP.md) · [LESSONS](LESSONS.md) · [CLAUDE](CLAUDE.md)

> ⚠️ The API currently has **no authentication** — see [SECURITY.md](SECURITY.md) before
> exposing it to any new network or caller.

---

## Requirements

- .NET 10 SDK
- PostgreSQL 14+ (for message logging)

---

## Local setup

```bash
# Clone and restore
git clone https://github.com/akalaico/Messenger.git
cd Messenger
dotnet restore

# Run the API (migrations run automatically on startup)
dotnet run --project Messenger.Api
```

The Postgres connection string key is `ConnectionStrings:UlakConnection` — the app fails
fast at startup if it is missing. Dev secrets are managed via `dotnet user-secrets` — see
`CLAUDE.md` for the configuration structure.

---

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/messages/sms` | Send SMS |
| POST | `/api/messages/email` | Send email |
| POST | `/api/messages/whatsapp` | Send WhatsApp message |
| POST | `/api/messages/push` | Send push notification |

### Request shapes

```json
// SMS
{ "to": ["+905551234567"], "text": "Your message" }

// Email
{ "to": ["user@example.com"], "subject": "Subject", "body": "Body text" }

// WhatsApp
{ "to": "+905551234567", "text": "Your message" }

// Push
{ "to": "device-fcm-token", "title": "Title", "body": "Body" }
```

---

## SMS Routing

SMS is routed by E.164 prefix due to regulatory requirements:

| Prefix | Provider |
|--------|----------|
| `+90` | Corvass (Turkey) |
| `+1` | Twilio (US / CA) |

Routing is configured in `appsettings.json` under `Sms:ProviderPrefixes`. Never hardcode provider selection.

---

## Deployment

Production runs as a Docker container on an Ubuntu VPS behind Nginx.

**VPS:** `root@187.124.233.239`

```bash
# 1. Publish (from repo root on Windows)
dotnet publish Messenger.Api/Messenger.Api.csproj -c Release -r linux-x64 --self-contained false -o ./publish

# 2. Upload to VPS
scp -O -r /c/Users/sarpa/source/repos/10/Messenger/publish/ root@187.124.233.239:~/apps/ulak-messenger/publish_new/
ssh root@187.124.233.239 "cp -r ~/apps/ulak-messenger/publish_new/. ~/apps/ulak-messenger/publish/ && rm -rf ~/apps/ulak-messenger/publish_new"

# 3. Rebuild and restart on VPS
ssh root@187.124.233.239 "cd ~/apps && docker compose build ulak-messenger && docker compose up -d"
```

The app listens on port **8080** inside the container, mapped to **5002** on the host. Nginx reverse-proxies external HTTPS traffic to port 5002.

---

## License

Internal project — not licensed for public distribution.
