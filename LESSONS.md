# ULAK Messenger — Failure Log

Narrative record of failures, design hazards, and production lessons for the ULAK messaging gateway.
Format per entry: **Symptom → Root Cause → Exact Fix**.

---

## 1. `AuthApi.Models` namespace on Messenger.Core options was a copy-paste artifact

**Context:** `CorvassOptions`, `TwilioOptions`, and `SmsOptions` in `Messenger.Core` were copied
from `Auth.Api` and adapted. Their namespace declaration was never updated.

**Symptom**
Files in `Messenger.Infrastructure` import `using AuthApi.Models;`. Every IDE and grep result
for `AuthApi.Models` returns hits inside the Messenger solution, making it appear that Messenger
depends on Auth's internal types. `namespace AuthApi.Models;` in a Core file also means the
options types are in the Auth namespace at runtime — any future consumer importing `Messenger.Core`
would also need to import an Auth-named namespace.

**Root Cause**
The three files were created via copy-paste from `Auth.Api/Models/` without updating the
`namespace` declaration. Because they compiled without errors (the namespace is a valid
identifier anywhere), the mistake was not caught at build time.

**Exact Fix**
Rename the namespace declaration in all three files and update every `using` that references it:
```bash
# In CorvassOptions.cs, TwilioOptions.cs, SmsOptions.cs:
namespace Messenger.Core.Models;   # was: namespace AuthApi.Models;

# In all Infrastructure files that consumed them:
using Messenger.Core.Models;       # was: using AuthApi.Models;
```
**Rule:** when `cp`-ing a file from one solution to another, the first edit is always the
namespace declaration. Never commit a file whose namespace does not match its project and folder.

---

## 2. `TwilioClient.Init()` in a constructor re-initialises global SDK state per request

**Context:** `TwilioSmsSender` is registered as Scoped. Its constructor calls:
```csharp
TwilioClient.Init(_options.AccountSid, _options.AuthToken);
```

**Symptom**
Under load, Twilio calls may use stale or partially-overwritten credentials. The `TwilioClient`
static REST client is a global singleton — `Init()` replaces it entirely. With a single
credential set and low traffic this is harmless. With concurrent requests, two `Init()` calls
race: one may set credentials while the other is mid-way through a REST call that reads the
same static client.

**Root Cause**
`TwilioClient.Init()` writes to a static field in the Twilio SDK, not to the `TwilioSmsSender`
instance. A Scoped service is created once per HTTP request — under concurrency, this means
`Init()` is called simultaneously by multiple request scopes, all mutating the same global
state.

**Exact Fix**
Call `TwilioClient.Init()` once at application startup in `Program.cs`, not in the constructor:
```csharp
// Program.cs (before builder.Build())
var twilioOptions = builder.Configuration.GetSection("Twilio").Get<TwilioOptions>()!;
TwilioClient.Init(twilioOptions.AccountSid, twilioOptions.AuthToken);
```
Remove the `TwilioClient.Init()` call from `TwilioSmsSender`'s constructor. The constructor
then only reads the already-initialised global client. If per-account isolation becomes
necessary, pass a `TwilioRestClient` instance into `MessageResource.CreateAsync` instead of
relying on the global default.

---

## 3. `CorvassSmsSender` injected as concrete type into `RoutingSmsSender` bypasses the interface

**Context:** `RoutingSmsSender` holds private fields typed as `CorvassSmsSender` and
`TwilioSmsSender` — the concrete classes — rather than `ISmsSender`.

**Symptom**
Adding a new SMS provider (e.g. Netgsm for secondary Turkish routing) requires modifying
`RoutingSmsSender`'s constructor signature and adding a new concrete field. There is no way
to mock `CorvassSmsSender` or `TwilioSmsSender` in unit tests without also registering and
constructing the real HTTP clients. The DI module also registers both as concrete types
(`services.AddScoped<CorvassSmsSender>()`).

**Root Cause**
The router was written when only two providers existed and the set was assumed to be fixed.
Injecting concrete types avoids any naming/keyed-registration complexity and is simpler to
read with exactly two providers. The tradeoff — loss of testability and extensibility — was
accepted as a pragmatic shortcut.

**Exact Fix**
Acceptable for the current two-provider set. If a third provider is added:
1. Introduce keyed DI registration (ASP.NET Core 8+):
   ```csharp
   services.AddKeyedScoped<ISmsSender, CorvassSmsSender>("Corvass");
   services.AddKeyedScoped<ISmsSender, TwilioSmsSender>("Twilio");
   ```
2. In `RoutingSmsSender`, inject `IServiceProvider` and resolve by key, or use a
   named-factory pattern.
3. Remove the concrete-type fields and the concrete-type registrations.

Do not add a third `ConcreteProvider` field — that path only gets harder.

---

## 4. `MessageLog` write is fire-and-forget — log gaps are invisible without monitoring

**Context:** `MessagesController.WriteLogAsync` saves a `MessageLog` row after each send.
Exceptions are caught, logged via `ILogger`, and swallowed — the send response is unaffected.

**Symptom**
A `MessageLog` row is silently missing. The send returned HTTP 200, the calling service
assumed the message was delivered, but when querying `MessageLog` for reporting there are
gaps. No alert fires; no error is returned to the caller.

**Root Cause**
The fire-and-forget design is correct: a DB write failure must not fail message delivery.
But without monitoring on the `LogError` output, swallowed exceptions produce invisible data
loss. A structured logging sink that is not configured (e.g. Seq endpoint down, Application
Insights key missing) means the `LogError` call also goes nowhere.

**Exact Fix**
The swallow-and-log approach is correct — do not change it to rethrow. Instead:
1. Confirm the structured logging sink is connected and verified in `docker logs ulak-service`
   after every deploy.
2. Set up an alert on `LogError` events from `MessagesController` — any occurrence means a
   log row is missing.
3. Optionally add a `MessageLog.Status` check endpoint for operational health (`GET
   /api/health/log-gap` reporting the last N minutes of write failures).

---

## 5. `ConsoleSmsService` fallback silently swallows production SMS sends for unknown prefixes

**Context:** `RoutingSmsSender.Resolve(string to)` looks up the provider name by iterating
`ProviderPrefixes` from config. If no prefix matches, it returns `_console` (a
`ConsoleSmsService` instance).

**Symptom**
A consuming app sends an SMS to a phone number whose prefix is not in `Sms:ProviderPrefixes`
(e.g. a UK `+44` number). The API returns HTTP 200. The caller believes the message was
delivered. The recipient never receives it. The only trace is a `Console.WriteLine` in the
service logs — easily missed without a log watcher.

**Root Cause**
`ConsoleSmsService` was designed as a dev-only fallback that logs to the console instead of
calling an external API. The fallback in `RoutingSmsSender` is intentional for development
(so unrecognised prefixes don't throw) but it should not be the production behaviour.

**Exact Fix**
In `RoutingSmsSender.Resolve`, throw when no prefix matches rather than returning the
console sender:
```csharp
throw new InvalidOperationException(
    $"No SMS provider configured for number prefix of '{to}'. " +
    $"Add a matching prefix to Sms:ProviderPrefixes in appsettings.");
```
`ConsoleSmsService` registration should be removed from the production DI module (or
feature-flagged behind `IsDevelopment()`). The controller will surface the exception as
HTTP 500, which is the correct signal: the operator is missing config for the requested
destination.

---

## 6. `MessengerDbContext` injected directly into `MessagesController` bypasses the service layer

**Context:** `MessagesController` constructor-injects `MessengerDbContext` alongside
`MessengerService`. The controller calls `WriteLogAsync`, which performs an EF Core `AddAsync` +
`SaveChangesAsync` directly.

**Symptom**
The controller now has two responsibilities: routing requests to the correct sender and
managing the message log persistence. Any test that covers the logging path must also wire
up a real or in-memory EF Core context. If the `MessageLog` schema changes, both the
Infrastructure layer and the controller need updating.

**Root Cause**
`WriteLogAsync` was added directly to the controller as a quick way to add logging without
creating a new service. The simplicity is real — but it places Infrastructure-layer code
(EF Core operations) inside the API layer, inverting the dependency arrow.

**Exact Fix**
Move `WriteLogAsync` into a dedicated `IMessageLogService` implemented in
`Messenger.Infrastructure`. The controller injects only the interface:
```csharp
// Messenger.Core/Interfaces/IMessageLogService.cs
public interface IMessageLogService
{
    Task WriteAsync(string channel, string recipient, string payload,
                   string status, string? correlationId);
}
```
Register in `MessengerInfrastructureModule`. The controller holds no EF Core dependency.
The `MessengerDbContext` is removed from the controller constructor. This is a non-breaking
refactor — the HTTP surface and send behaviour do not change.

---

## 7. Duplicate config sections `Messaging:CorvassApi` and `Corvass` for the same provider

**Context:** `appsettings.json` has two sections that both describe Corvass credentials:
- `Messaging:CorvassApi:{ SmsUrl, ApiKey, ApiSecret }`
- `Corvass:{ SmsUrl, ApiKey, ApiSecret, Originator, MessageType, RecipientType }`

**Symptom**
An operator setting production secrets via environment variables does not know which key
to set. Both `Messaging__CorvassApi__ApiKey` and `Corvass__ApiKey` exist in the codebase.
Setting the wrong one causes `CorvassSmsSender` to use an empty or default credential and
every Corvass SMS call fails with HTTP 401 from the Corvass API.

**Root Cause**
`MessengerInfrastructureModule` binds `CorvassOptions` to the `"Corvass"` section.
The `Messaging:CorvassApi` section is a legacy remnant from an earlier config structure
that was never cleaned up. The live code reads from `"Corvass"`, so `Messaging:CorvassApi`
is effectively dead — but it appears in `appsettings.json` with matching key names.

**Exact Fix**
Remove the `Messaging:CorvassApi` section from `appsettings.json` and all its counterparts
(`appsettings.Development.json`, `appsettings.Production.json`, `.env` templates).
Keep only the `Corvass:` section. Update the production `.env` key prefix accordingly:
```
Corvass__SmsUrl=...
Corvass__ApiKey=...
Corvass__ApiSecret=...
Corvass__Originator=AKAL YNT.
```
Verify `MessengerInfrastructureModule` still binds to `"Corvass"` — no code change required,
only config cleanup.

---

## 8. `publish/` is gitignored — do not use `git add -A` around a deploy cycle

**Context:** The `dotnet publish` output directory (`publish/`) is listed in `.gitignore`.

**Symptom**
A developer runs `git add -A` after `dotnet publish` and then `git status` — the binaries
are excluded as expected. However, running `git add .` in some older Git versions on
Windows may include some binary artefacts if the `.gitignore` is not consulted correctly.
Committing a 10–50 MB binary blob into a .NET project repo slows every `git clone` and
`git pull` permanently.

**Root Cause**
`dotnet publish -o ./publish` places framework DLLs, the entry-point binary, and
`appsettings.json` into `publish/`. The directory is gitignored precisely to prevent this.
`git add -A` is safe; `git add .` should be verified with `git status` before committing.

**Exact Fix**
Always check `git status` before committing near a deploy cycle. The safe habit:
```bash
git status            # verify no publish/ artefacts appear
git add Messenger.Api/Controllers/NewController.cs   # add specific files, not -A or .
git commit -m "feat: ..."
```
If binary artefacts do get committed, remove them with `git rm -r --cached publish/` and
add a `git commit --amend` (if not yet pushed) or a follow-up commit.
