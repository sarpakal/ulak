# ULAK Messenger Solution — Failure Log

Cross-project failures and operational lessons that span multiple projects in this solution.
Format per entry: **Symptom → Root Cause → Exact Fix**.

Per-project failure logs live in each project's own `LESSONS.md`:
- [`Messenger.Api/LESSONS.md`](Messenger.Api/LESSONS.md) — best-effort MessageLog, DbContext in controller
- [`Messenger.Core/LESSONS.md`](Messenger.Core/LESSONS.md) — namespace artifact, duplicate options types
- [`Messenger.Infrastructure/LESSONS.md`](Messenger.Infrastructure/LESSONS.md) — Twilio global Init, concrete-type routing, console fallback, retry-loop `when`-filter dead code

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

## 2. Duplicate config sections `Messaging:CorvassApi` and `Corvass` for the same provider

**Context:** `appsettings.json` has two sections that both describe Corvass credentials:
- `Messaging:CorvassApi:{ SmsUrl, ApiKey, ApiSecret }` — bound to `CorvassApiOptions` in `Program.cs`
- `Corvass:{ SmsUrl, ApiKey, ApiSecret, Originator, MessageType, RecipientType }` — bound to
  `CorvassOptions` in `MessengerInfrastructureModule`

**Symptom**
An operator setting production secrets via environment variables does not know which key
to set. Both `Messaging__CorvassApi__ApiKey` and `Corvass__ApiKey` exist in the codebase.
Setting the wrong one causes `CorvassSmsSender` to use an empty or default credential and
every Corvass SMS call fails with HTTP 401 from the Corvass API.

**Root Cause**
The live code reads from `"Corvass"`. The `Messaging:CorvassApi` section is a legacy remnant
from an earlier config structure that was never cleaned up — effectively dead, but it appears
in `appsettings.json` with matching key names.

**Exact Fix (resolved 2026-07-11)**
Removed the `Messaging:CorvassApi` section from the appsettings files, deleted `CorvassApiOptions`
and its `Program.cs` binding; only the `Corvass:` section remains (bound in
`MessengerInfrastructureModule` — no sender code change, config cleanup only):
```
Corvass__SmsUrl=...
Corvass__ApiKey=...
Corvass__ApiSecret=...
Corvass__Originator=AKAL YNT.
```
Operators must still purge any stale `Messaging__CorvassApi__*` keys from the live VPS `.env`
(the repo can't reach it). Live key prefix is `Corvass__*`.

---

## 3. `publish/` is gitignored — do not use `git add -A` around a deploy cycle

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
a `git commit --amend` (if not yet pushed) or a follow-up commit.

---

## 4. Safety-critical fallbacks must fail *closed* — prefer a config flag over environment detection

**Context:** `RoutingSmsSender` fell back to `ConsoleSmsService` (log + HTTP 200, no delivery)
for any recipient prefix that matched no provider. The obvious fix was "only do that in
Development" — i.e. gate the fallback on `IHostEnvironment.IsDevelopment()`. See the full
incident in [Infrastructure LESSONS](Messenger.Infrastructure/LESSONS.md) #3.

**Symptom / hazard**
Environment-gated safety reproduces the very failure it's meant to prevent. `IsDevelopment()`
reads `ASPNETCORE_ENVIRONMENT`; if that variable is unset, mis-set, or defaults differently on
the VPS than assumed, the dangerous behaviour (silent message loss behind a 200) silently
switches back on. Safety then depends on an ambient string being correct in every environment.

**Root Cause**
Coupling a safety decision to environment detection makes "safe" the *conditional* branch and
"dangerous" the fallthrough. Any gap in environment configuration lands you in the dangerous
branch by default.

**Exact Fix — the transferable rule**
Make the dangerous behaviour an explicit, named, default-`false` opt-in in the app's own
config surface, so *absence of configuration is safe*:
```csharp
public bool AllowConsoleFallback { get; init; } = false;   // default = fail closed
```
Enable it only in `appsettings.Development.json` (which is gitignored here, so a fresh clone
also fails closed). Do **not** inject `IHostEnvironment` into transport/service code to make
this decision — it violates the options-pattern rule (solution [CLAUDE.md](CLAUDE.md) rule #5)
and ties correctness to hosting semantics. Applies platform-wide: any base-service fallback,
degraded-mode switch, or "skip the real call" shortcut should fail closed by default and be
enabled by explicit config, never by environment name.

**Corollary (testing):** the retry-exhaustion bug in
[Infrastructure LESSONS](Messenger.Infrastructure/LESSONS.md) #4 was found because the new test
asserted the *documented contract* (`SmsException` after exhaustion), not the observed
behaviour. Write contract-first assertions — they catch code that silently drifted from its
own doc comment.
