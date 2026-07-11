# CLAUDE.md — Messenger.Infrastructure

Provider implementations + EF Core + DI module. Implements `Messenger.Core` interfaces.

> Failure log: [LESSONS.md](LESSONS.md) · solution rules: [../CLAUDE.md](../CLAUDE.md)

---

## Hard rules

1. **All DI lives in `MessengerInfrastructureModule`.** New sender → register it there,
   with Polly policies if it owns an HttpClient.
2. **Provider selection is config-driven.** `RoutingSmsSender` resolves via
   `SmsOptions.ResolveProvider` (`Sms:ProviderPrefixes`). Never hardcode a provider choice.
3. **Options pattern only** — `IOptions<T>`, never `IConfiguration`.
4. **EF Core + Npgsql versions move together** (`10.0.x`). Migrations are applied by the
   Api on startup; after `dotnet ef migrations add`, confirm both the `.cs` **and**
   `.Designer.cs` are staged (platform-wide pitfall — a missing designer file makes
   `Migrate()` silently skip the migration).

---

## Known design debt (documented, do not extend)

| Debt | Hazard | Planned fix |
|------|--------|-------------|
| `TwilioClient.Init()` in `TwilioSmsSender` ctor | Global SDK state mutated per request scope — credential race under concurrency | Init once in `Program.cs` ([LESSONS](LESSONS.md) #1) |
| `RoutingSmsSender` injects concrete `CorvassSmsSender`/`TwilioSmsSender` | Untestable, unextendable | Keyed DI when a 3rd provider arrives ([LESSONS](LESSONS.md) #2) — do **not** add a third concrete field |
| ~~`ConsoleSmsService` as production fallback for unknown prefixes~~ | ~~HTTP 200 + no delivery = silent message loss~~ | ✅ **Fixed** — `RoutingSmsSender.Resolve` throws `SmsException` on unmatched prefix and unknown provider name; console gated behind `Sms:AllowConsoleFallback` (dev only, default `false`). ([LESSONS](LESSONS.md) #3, [ROADMAP](../ROADMAP.md) Phase 2) |

---

## Adding a new SMS provider checklist

1. `NewProviderSmsSender : ISmsSender` in `Senders/`, options type in `Messenger.Core`
2. Register in `MessengerInfrastructureModule` — this is the moment to switch to keyed DI
   (`AddKeyedScoped<ISmsSender, T>("Name")`) and refactor `RoutingSmsSender.Resolve`
3. Add the prefix → provider-name mapping to `Sms:ProviderPrefixes` (all appsettings + `.env`)
4. Polly policies + handler lifetime on its HttpClient, same as `CorvassSmsSender`
