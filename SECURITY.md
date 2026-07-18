# Security Model

How ULAK protects (and currently fails to protect) the messaging gateway.
Architecture: [ARCHITECTURE.md](ARCHITECTURE.md). Open items: [ROADMAP.md](ROADMAP.md).

---

## Threat model summary

ULAK must prevent:

1. **Unauthorized message dispatch** — anyone reaching the API can send SMS/Email/WhatsApp/Push
   at the operator's expense and under the operator's sender identity (`AKAL YNT.` originator).
2. **Provider credential exfiltration** — Corvass/Twilio/SMTP/WABA/FCM credentials are
   high-value: they allow unlimited paid sends outside ULAK entirely.
3. **Message content leakage** — payloads transit ULAK and are persisted in `MessageLogs`.
4. **Silent delivery failure** — a message the caller believes was sent but never was
   (an availability/integrity concern, not just a bug).

---

## Current state: network-layer allowlist in place; no app-level auth yet

`Program.cs` still registers **no authentication or authorization** (the `Add Authentication & JWT`
section is an empty placeholder). As of **2026-07-17**, however, `deploy/nginx/ulak.conf`
(mirrored into the live `apis.conf`) restricts `/api/messages/*` with an nginx `allow`/`deny`
allowlist — only the `apps_appnet` container subnet (`172.19.0.0/16`), the n8n subnet
(`172.18.0.0/16`), `127.0.0.1`, and two known dev-machine IPs may send; everyone else gets `403`.
`/health` stays public for monitoring. The allow set was derived from 2 weeks of access logs.

This closes the "free SMS cannon" exposure at the network layer, but it is **not** app-level
authentication:
- Any host that lands inside the allowed subnets (or spoofs a dev IP on-path) can still send.
- Dev-machine IPs are dynamic residential addresses — they need updating if they change.

Remaining options, in increasing order of effort (tracked in [ROADMAP.md](ROADMAP.md)):
1. ~~Nginx `allow`/`deny`~~ — **done** (see above).
2. Static API key header checked by middleware (`X-Api-Key` from `~/apps/.env`) — better fit
   for dev-machine access than a hardcoded IP.
3. Auth.Api-issued JWT validation (`app_code` claim per calling service).

Until app-level auth lands: keep the ULAK URL unadvertised and monitor `MessageLogs` for
unexpected channels/recipients.

---

## Provider credentials

| Credential | Grants | Storage |
|------------|--------|---------|
| `Corvass:ApiKey` / `ApiSecret` | Paid SMS sends as `AKAL YNT.` (Turkey) | `~/apps/.env` prod; user-secrets dev |
| `Twilio:AccountSid` / `AuthToken` | Full Twilio account API access | same |
| `Messaging:Email:SenderPassword` | SMTP mailbox | same |
| `Messaging:Whatsapp:ApiKey` | WABA sends | same |
| `Messaging:FcmNotification:ServerKey` | Push to all app users | same |

- Never commit real values; `appsettings.json` ships empty strings.
- Corvass secrets live under `Corvass__*`. (The old duplicate `Messaging:CorvassApi` dead-config
  section — an operator trap that silently broke sends — was removed 2026-07-11, [LESSONS.md](LESSONS.md) #7.)
- The Twilio auth token is account-wide; rotate it at Twilio if the `.env` is ever exposed.

---

## Data at rest — MessageLogs stores payloads in plaintext

Every send writes `Channel`, `Recipient`, `Payload` (SMS text / email subject / push title),
`Status`, `CorrelationId`, `CreatedAt` to Postgres:

- **Phone numbers and email addresses are PII** under KVKK/GDPR.
- **Payloads may contain secrets** — if OTP dispatch is ever consolidated through ULAK
  (roadmap candidate), `Payload` would contain live OTP codes. Callers must assume message
  bodies are persisted.
- **Retention:** as of 2026-07-17 a background `MessageLogRetentionJob` deletes rows older than
  `MessageLogRetention:RetentionDays` (default 90) every `RunIntervalHours` (default 24), so
  `MessageLogs` no longer grows unbounded. It fails safe — a `RetentionDays < 1` misconfiguration
  skips the run rather than wiping the table. Tune the window per data-minimisation needs.

---

## Delivery integrity

- Provider failures throw after retries (`SmsException` → HTTP 500), so callers get a true
  failure signal.
- **Exception:** unmatched SMS prefixes fall back to `ConsoleSmsService`, which logs to
  console and returns success — the caller gets HTTP 200 and the recipient gets nothing
  ([Messenger.Infrastructure/LESSONS.md](Messenger.Infrastructure/LESSONS.md)). Fix
  (throw on unknown prefix in production) is on the roadmap.
- `MessageLog` writes are best-effort by design; a write failure is an `ILogger` error only.
  Alert on `MessageLog write failed` log events — each one is a reporting gap.

---

## Transport security

- External TLS terminates at Nginx (Certbot/Let's Encrypt, auto-renewing); the container
  speaks plain HTTP on the Docker network.
- The VPS-internal `http://<vps-ip>/messenger/` route is unencrypted HTTP — acceptable only
  for same-host callers (n8n, platform containers).
- Outbound provider calls (Corvass, WABA, FCM, Twilio) are HTTPS with Polly retry + timeout.

---

## Known limitations (tracked in [ROADMAP.md](ROADMAP.md))

- No authentication on any endpoint (see above) — highest priority.
- No rate limiting — combined with no auth, the API is a free SMS cannon until fixed.
- `ConsoleSmsService` production fallback converts misconfiguration into silent message loss.
- ~~No `MessageLogs` retention; payloads (PII) accumulate indefinitely.~~ **Fixed 2026-07-17** —
  `MessageLogRetentionJob` prunes rows past the configured window (default 90 days).
- `TwilioClient.Init()` per-request global mutation is a credential race under concurrency.
- No test coverage of routing or failure paths.
