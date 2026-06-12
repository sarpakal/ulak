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
