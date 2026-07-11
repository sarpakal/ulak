# Messenger.Core — Failure Log

Project-specific failures. Format per entry: **Symptom → Root Cause → Exact Fix**.
Cross-project entries live in the solution [LESSONS.md](../LESSONS.md).

---

## 1. Copied options files kept their source solution's namespace

**Context:** `CorvassOptions`, `TwilioOptions`, and `SmsOptions` (now in `Models/`) were
copied from `Auth.Api/Models/` when the SMS layer was ported into ULAK.

**Symptom**
The files declared `namespace AuthApi.Models;` and every Infrastructure consumer imported
`using AuthApi.Models;`. Grep results for Auth types returned hits inside the Messenger
solution, and any future consumer of `Messenger.Core` would have needed an Auth-named
namespace import.

**Root Cause**
Copy-paste without updating the namespace declaration. It compiles fine — a namespace is a
valid identifier anywhere — so nothing caught it at build time.

**Exact Fix**
Renamed to `namespace Messenger.Core.Models;` in all three files and updated every consumer
(`using Messenger.Core.Models;`). Fixed in commit `5825945`.

**Rule:** when copying a file across solutions, the first edit is the `namespace` line.
Full entry with diff detail: solution [LESSONS.md](../LESSONS.md) #1.

---

## 2. Two options types describe the same provider — one is dead

**Context:** `Options/CorvassApiOptions` (bound from `Messaging:CorvassApi` in `Program.cs`)
and `Models/CorvassOptions` (bound from `Corvass:` in the Infrastructure module) coexist.

**Symptom**
Operators setting `Messaging__CorvassApi__ApiKey` in production see every Corvass send fail
with HTTP 401 — the live sender reads `CorvassOptions` from the `Corvass:` section.

**Root Cause**
`CorvassApiOptions` is a remnant of an earlier config structure that was never removed after
the `Corvass:` binding became the live path.

**Exact Fix (resolved 2026-07-11)**
Deleted `CorvassApiOptions` and its `Program.cs` binding together with the
`Messaging:CorvassApi` sections in all appsettings files; only `Corvass:` remains.
Config-level detail in solution [LESSONS.md](../LESSONS.md) #7. Lesson: a copied-in
options type that is bound but never *read* is dead weight that actively misleads
operators — delete it when the live binding supersedes it, don't leave it "just in case".
