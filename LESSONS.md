# ULAK Messenger Solution — Failure Log

Cross-project failures and operational lessons that span multiple projects in this solution.
Format per entry: **Symptom → Root Cause → Exact Fix**.

Per-project failure logs live in each project's own `LESSONS.md`:
- [`Messenger.Api/LESSONS.md`](Messenger.Api/LESSONS.md) — best-effort MessageLog, DbContext in controller
- [`Messenger.Core/LESSONS.md`](Messenger.Core/LESSONS.md) — namespace artifact, duplicate options types
- [`Messenger.Infrastructure/LESSONS.md`](Messenger.Infrastructure/LESSONS.md) — Twilio global Init, concrete-type routing, console fallback

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

**Exact Fix**
Remove the `Messaging:CorvassApi` section from all appsettings files and `.env` templates,
delete `CorvassApiOptions` and its `Program.cs` binding, and keep only the `Corvass:` section:
```
Corvass__SmsUrl=...
Corvass__ApiKey=...
Corvass__ApiSecret=...
Corvass__Originator=AKAL YNT.
```
Verify `MessengerInfrastructureModule` still binds to `"Corvass"` — no sender code change
required, only config cleanup. Tracked in [ROADMAP.md](ROADMAP.md) Phase 2.

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
