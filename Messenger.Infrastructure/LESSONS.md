# Messenger.Infrastructure — Failure Log

Project-specific failures and design hazards. Format per entry: **Symptom → Root Cause → Exact Fix**.
Cross-project entries live in the solution [LESSONS.md](../LESSONS.md).

---

## 1. `TwilioClient.Init()` in a constructor re-initialises global SDK state per request

**Context:** `TwilioSmsSender` is registered as Scoped. Its constructor calls
`TwilioClient.Init(_options.AccountSid, _options.AuthToken)`.

**Symptom**
Under load, Twilio calls may use stale or partially-overwritten credentials. With a single
credential set and low traffic this is harmless; with concurrent requests, two `Init()`
calls race — one may set credentials while the other is mid-way through a REST call reading
the same static client.

**Root Cause**
`TwilioClient.Init()` writes to a static field in the Twilio SDK, not to the sender
instance. A Scoped service is constructed once per request scope, so under concurrency
multiple scopes mutate the same global state simultaneously.

**Exact Fix**
Initialise once at startup in `Program.cs` (before `builder.Build()`):
```csharp
var twilioOptions = builder.Configuration.GetSection("Twilio").Get<TwilioOptions>()!;
TwilioClient.Init(twilioOptions.AccountSid, twilioOptions.AuthToken);
```
Remove the `Init()` call from the constructor. If per-account isolation becomes necessary,
pass a `TwilioRestClient` instance into `MessageResource.CreateAsync` instead of relying on
the global default. Tracked in [ROADMAP](../ROADMAP.md) Phase 3.

---

## 2. `CorvassSmsSender` injected as concrete type into `RoutingSmsSender` bypasses the interface

**Context:** `RoutingSmsSender` holds fields typed as `CorvassSmsSender`, `TwilioSmsSender`,
and `ConsoleSmsService` — concrete classes, not `ISmsSender`. The DI module registers the
concrete types directly.

**Symptom**
Adding a new SMS provider requires changing `RoutingSmsSender`'s constructor and adding
another concrete field. Unit-testing the router requires constructing real senders with
their HTTP clients.

**Root Cause**
Written when only two providers existed and the set was assumed fixed; concrete injection
avoided keyed-registration complexity. The testability/extensibility trade-off was accepted
as a pragmatic shortcut.

**Exact Fix**
Acceptable for the current provider set. When a third provider is added:
```csharp
services.AddKeyedScoped<ISmsSender, CorvassSmsSender>("Corvass");
services.AddKeyedScoped<ISmsSender, TwilioSmsSender>("Twilio");
```
Resolve by key in `RoutingSmsSender` (or use a named factory), remove the concrete fields
and registrations. Do **not** add a third concrete field — that path only gets harder.

---

## 3. `ConsoleSmsService` fallback silently swallows production SMS for unknown prefixes

**Context:** `RoutingSmsSender` maps each recipient via `SmsOptions.ResolveProvider`; a null
result falls back to `"Console"` → `ConsoleSmsService`.

**Symptom**
A caller sends SMS to a prefix not in `Sms:ProviderPrefixes` (e.g. `+44`). The API returns
HTTP 200; the recipient never receives anything. The only trace is a console log line —
easily missed.

**Root Cause**
`ConsoleSmsService` is a dev-only fallback so unrecognised prefixes don't throw during
development, but the same path runs in production, converting a configuration gap into
silent message loss with a success response.

**Exact Fix**
Throw on unmatched prefixes outside development:
```csharp
throw new InvalidOperationException(
    $"No SMS provider configured for number prefix of '{to}'. " +
    $"Add a matching prefix to Sms:ProviderPrefixes.");
```
Gate the `ConsoleSmsService` registration behind `IsDevelopment()` (or remove it from the
production DI path). The resulting HTTP 500 is the correct signal: the operator is missing
config for the requested destination. Tracked in [ROADMAP](../ROADMAP.md) Phase 2.

**Resolution (2026-07-10)** — Fixed, but *not* via environment-gated registration as
originally proposed above. Gating on `IsDevelopment()` reproduces the same failure class: it
depends on `ASPNETCORE_ENVIRONMENT` being correct on the VPS, so a misset env var silently
restores the console fallback. Instead the fallback is now fail-closed by default:

- `SmsOptions.AllowConsoleFallback` (default `false`). Absence of config → safe.
- `RoutingSmsSender.SendAsync` no longer coerces unmatched prefixes to `"Console"` — it keeps
  the `null` from `ResolveProvider` and lets `Resolve` decide.
- `RoutingSmsSender.Resolve` throws `SmsException` on an unmatched prefix (`null`) unless
  `AllowConsoleFallback` is true, **and** throws on a prefix that maps to a provider name with
  no registered sender (e.g. `+44` → `"Vodafone"`) — the old `_ => _console` default silently
  swallowed that config-mismatch case too.
- `Sms:AllowConsoleFallback: true` set only in `appsettings.Development.json`. Base and
  Production appsettings are untouched.

`MessagesController.SendSms` already rethrows on send failure, so the `SmsException` surfaces
as HTTP 500 with a `Failed` `MessageLog` row — no controller change was needed. Using
`SmsException` (not `InvalidOperationException`) keeps the provider/recipient context that the
router's other failure paths already carry.

---

## 4. Retry loop lost provider context on the final attempt — `SmsException` was dead code

**Context:** `RoutingSmsSender.SendWithRetryAsync` retries a failing provider `RetryCount + 1`
times, then (per its XML doc and [ROADMAP](../ROADMAP.md) Phase 1 "SmsException with provider
context") is meant to throw `SmsException` wrapping the last failure.

**Symptom**
After all retries were exhausted, callers received the raw provider exception
(`HttpRequestException` from `CorvassSmsSender`) instead of `SmsException` — losing the
provider name and recipient list. Surfaced by `SendAsync_ProviderFailsEveryAttempt_...` in
`RoutingSmsSenderTests` (added 2026-07-10), which asserted the documented `SmsException`.

**Root Cause**
The catch used an exception filter: `catch (Exception ex) when (attempt < totalAttempts)`.
On the **last** attempt the filter is false, so the catch never fires — the provider
exception propagates straight out of the method and the `throw new SmsException(...)` after
the loop is unreachable on the failure path (dead code). It only ever "worked" when
`RetryCount` produced ≥2 attempts *and* a caller happened not to distinguish exception types.

**Exact Fix**
Catch every attempt and gate the retry/delay on attempts remaining, so `lastEx` is always
captured and the loop falls through to the wrapping throw:
```csharp
catch (Exception ex)
{
    lastEx = ex;
    if (attempt < totalAttempts)
    {
        _logger.LogWarning(ex, "...Retrying in {Delay}ms...", attempt, providerName, _options.RetryDelayMs);
        await Task.Delay(_options.RetryDelayMs, ct);
    }
}
// ... loop ends ...
throw new SmsException(..., lastEx);
```
Lesson: an exception *filter* (`when`) that references the loop counter silently changes
behavior on the boundary iteration. Prefer catching unconditionally and branching inside.

---

## 5. Npgsql / EFCore.Design version drift is a latent CS1705 — harmless until a consumer compiles against EF types

**Context:** `Messenger.Infrastructure.csproj` references `Microsoft.EntityFrameworkCore.Design`
`10.0.7` and `Npgsql.EntityFrameworkCore.PostgreSQL` `10.0.1`. Npgsql `10.0.1` depends on EF
Core `10.0.4`, so Infrastructure is *compiled* against EF Core `10.0.7` (from Design) while its
primary EF Core reference resolves to `10.0.4`.

**Symptom**
The main solution builds with only an `MSB3277` warning ("Found conflicts between different
versions of Microsoft.EntityFrameworkCore"). But when a new project references Infrastructure
**and uses EF types directly** (e.g. `Messenger.Tests` calling `MessengerDbContext` /
`MigrateAsync` for the integration tests), the warning escalates to a hard build error:
`CS1705: Assembly 'Messenger.Infrastructure' uses 'Microsoft.EntityFrameworkCore, Version=10.0.7.0'
which has a higher version than referenced assembly '…10.0.4.0'`.

**Root Cause**
The package set drifted: `EFCore.Design` was bumped to `10.0.7` without moving Npgsql (whose EF
Core pin lags at `10.0.4`). CLAUDE.md's rule "EF Core + Npgsql versions move together" was not
followed on that bump.

**Exact Fix (resolved 2026-07-11)**
Add explicit **public** EF Core references to `Messenger.Infrastructure` at the same version it
compiles against, so the whole graph is coherent and consumers inherit `10.0.7` (not Npgsql's
`10.0.4` floor). `EFCore.Design` stays `PrivateAssets=all` (tooling only); `EFCore` + `Relational`
are the public, move-together pair:
```xml
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.7" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.7" />
```
The earlier test-project-local workaround pins were removed — consumers no longer need their own
pin. Lesson: a `PrivateAssets=all` package (like `EFCore.Design`) does **not** flow its transitive
version to consumers, so if it is the only thing pulling your real EF Core version, add an explicit
public `Microsoft.EntityFrameworkCore` reference alongside it. Keep `EFCore` and `EFCore.Relational`
on the same version, and confirm the Npgsql provider's floor is ≤ that version.
