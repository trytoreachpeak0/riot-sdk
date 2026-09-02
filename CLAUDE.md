# riot-sdk

The RIoT SDK for the RCS interface — a C# implementation under `csharp/` and a
Python one under `python/`, sharing `specs/`, `docs/` and `scripts/`. Split out of
`8005---AGV` on 2026-09-02 (that repository is now `8005-agv-program`).

The name carries no `8005-` prefix on purpose: RIoT is the RCS vendor's
interface, not something specific to the 8005 project, and
`8005-agv-control-server` already consumes it as an ordinary package.

## Write authority

This repository is writable. Elsewhere in the workspace:
`8005-agv-control-server`, `8005-mes-ingest` and `8005-agv-program` are writable;
`8005-agv-onboard-hmi` and `slots-simulator` are read-only for agents;
`8005-agv-protocol` is writable but every push there must be announced to Kun
Wang in an issue that `@SocialKKKK`.

## This SDK ships as a package, and a consumer is pinned to it

`8005-agv-control-server` references `RIoT.Sdk.Facade` as a `PackageReference`
pinned to an exact version (`0.1.0-controlserver.2` as of the split), resolved
from a local feed it vendors at `vendor/nuget/riot-sdk/<version>/`. It does
**not** take a project reference on this repository.

So a change here reaches that consumer only when someone builds a new package
version and vendors it. **Do not assume an edit here is live for the control
server**, and do not change the published surface of `RIoT.Sdk.Facade` without
saying which consumer version has to move.

## Tests

**"Run the full test suite" means this command:**

```powershell
dotnet test csharp/RIoT.Sdk.Tests
```

Test authorization is scoped to the current task. A request to inspect, tidy,
commit, or push an already-dirty worktree does **not** authorize a test run.
Never infer it from `git status`.

## Two implementations, one contract

`csharp/` and `python/` implement the same RIoT surface. A change to the shared
contract in `specs/` affects both. When you change one language's implementation
to match a contract change, say explicitly whether the other one still matches —
do not leave the question open.

## Language

Agent instruction files — this one, and anything under `.claude/` — are written in
**English**.

Everything a human reads is written in **Chinese**: README files, documentation
prose, ADR bodies, issue and pull-request titles and bodies, and commit message
bodies.

Stay English inside Chinese text: conventional commit prefixes (`feat:`, `fix:`,
`docs:`, `chore:`), identifiers, paths, commands, environment variables, error
codes, and RIoT API names, thing-model values and endpoint paths — those come
from the vendor's interface and must never be translated. Quote an error or a
test result in its original English first, then explain it in Chinese. Do not
rewrite existing text to match; this governs new writing.

## Scripting baseline

PowerShell 7. Do not write Windows PowerShell 5.1 compatible code, do not add
version probes or fallbacks, and do not invoke `powershell.exe` — call `pwsh`.
Every new `.ps1` opens with `#Requires -Version 7`.
