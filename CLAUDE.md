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

## Agent skills

### Issue tracker

Issues and specs live as GitHub issues. See `docs/agents/issue-tracker.md`.
External pull requests are treated as a request surface and run through the same
triage labels — that flag is on.

### Triage labels

The five canonical roles (`needs-triage`, `needs-info`, `ready-for-agent`,
`ready-for-human`, `wontfix`), created in this repository. See
`docs/agents/triage-labels.md`.

### Domain docs

`CONTEXT.md` at the repository root plus `docs/adr/`. See `docs/agents/domain.md`.

### Matt Pocock's skills

Installed as the `mattpocock-skills` plugin (user-level). Invoke them namespaced:
`/mattpocock-skills:<name>`. They are explicit-only — use one when the user names
it. `code-review` collides with the bundled `/code-review`; use
`/mattpocock-skills:code-review` for the Standards+Spec review.

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

## Toolchain baseline

This repository is pinned to the workspace-wide .NET toolchain. The authority is
`8005-agv-program/docs/adr/cross/0056-dotnet-toolchain-baseline.md`.

| Item | Pinned value | Enforced by |
| --- | --- | --- |
| SDK | 8.0.424, `rollForward: disable` | `global.json` |
| Target framework | `net8.0` | `Directory.Build.props` |
| Test stack | xunit.v3 3.2.2, Microsoft.NET.Test.Sdk 18.8.1, xunit.runner.visualstudio 3.1.5 | `Directory.Packages.props` |
| Banned packages | xunit v2, NUnit, MSTest, coverlet.collector | `Directory.Build.targets` |

Package versions live in `Directory.Packages.props` and nowhere else. xunit v2,
NUnit, MSTest and coverlet.collector are banned — do not add them back, and do
not "upgrade" a test project by switching frameworks. `Directory.Build.targets`
enforces that at build time: a banned package produces `error W2G0056` and fails
the build. It matches item identity exactly, so `xunit.v3` is not caught by the
`xunit` entry. Verified on 2026-09-04 by adding one deliberately.

**An inline `Version=` is a different story, and this file used to get it
wrong.** It does not fail restore with NU1008. Under central package management
NuGet silently ignores it — the central version wins, no error, no warning, and
`%(PackageReference.Version)` is empty in every MSBuild target, so no build-time
guard can see it either. `CentralPackageVersionOverrideEnabled=false` governs the
`VersionOverride` attribute, not `Version`. The version therefore never actually
drifts, but whoever wrote the inline one is not told it was ignored. Only
`check-toolchain.ps1` catches it, by reading the csproj as text. Do not write an
MSBuild target for it — one was written and deleted after it passed every case it
existed to fail.

Never raise a version in one repository alone. Change the ADR and every
repository together, then run `check-toolchain.ps1` from the workspace root; it
reports drift across all seven repositories and exits non-zero when a writable
one deviates.

## Scripting baseline

PowerShell 7. Do not write Windows PowerShell 5.1 compatible code, do not add
version probes or fallbacks, and do not invoke `powershell.exe` — call `pwsh`.
Every new `.ps1` opens with `#Requires -Version 7`.
