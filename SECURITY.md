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

## ⚠️ Current state: the API is unauthenticated

`Program.cs` registers **no authentication or authorization** (the `Add Authentication & JWT`
section is an empty placeholder) and `deploy/nginx/ulak.conf` applies **no IP allowlist** —
`https://ulak.akgyh.com/api/messages/*` is publicly callable by anyone on the internet.

Mitigations in place today:
- The URL is not advertised; callers are platform services and n8n on the same VPS.
- Provider spend limits (Corvass account balance, Twilio limits) cap the blast radius.

This is **the** outstanding security gap. Options, in increasing order of effort
(tracked in [ROADMAP.md](ROADMAP.md)):
1. Nginx `allow`/`deny` on the `ulak.akgyh.com` server block (VPS-internal callers only) —
   note n8n and platform containers reach it via the internal route, so external exposure
   may be removable outright.
2. Static API key header checked by middleware (`X-Api-Key` from `~/apps/.env`).
3. Auth.Api-issued JWT validation (`app_code` claim per calling service).

Until one lands: treat the ULAK URL itself as a secret and monitor `MessageLogs` for
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
- The duplicate `Messaging:CorvassApi` section is dead config — setting secrets there
  silently breaks Corvass sends ([LESSONS.md](LESSONS.md) #7). Live key prefix: `Corvass__*`.
- The Twilio auth token is account-wide; rotate it at Twilio if the `.env` is ever exposed.

---

## Data at rest — MessageLogs stores payloads in plaintext

Every send writes `Channel`, `Recipient`, `Payload` (SMS text / email subject / push title),
`Status`, `CorrelationId`, `CreatedAt` to Postgres:

- **Phone numbers and email addresses are PII** under KVKK/GDPR.
- **Payloads may contain secrets** — if OTP dispatch is ever consolidated through ULAK
  (roadmap candidate), `Payload` would contain live OTP codes. Callers must assume message
  bodies are persisted.
- There is **no retention job** — `MessageLogs` grows unbounded. A retention policy
  (pattern: Auth.Api's `AuditRetentionJob`) is on the roadmap.

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
- No `MessageLogs` retention; payloads (PII) accumulate indefinitely.
- `TwilioClient.Init()` per-request global mutation is a credential race under concurrency.
- No test coverage of routing or failure paths.
