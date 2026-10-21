# Aviary

A native Windows browser built with WinUI 3 and Microsoft Edge WebView2. Alpha Build 5
is **The Long Run**, version 0.5.0, with all sixteen milestones complete. The AB5
release passed Windows CI. The latest Settings follow-up is tested and installed
locally; its source is still uncommitted/unpushed and has not run on GitHub CI.
macOS support and starting Beta Build 1 remain deferred.

Classic and Liquid Glass looks, tabs and tab groups, Bird News, Shields, offline
Collection exhibits, and optional Quik prepared tabs are implemented. Quik is in Beta;
its spare tabs use additional memory. Per-tab audio controls and loading indicators
arrived in M8. Measurements and limitations are recorded in the verification notes.

## Latest local update — 21 October 2026

Privacy/security follow-up is ready for approval, separately from the installed Settings copy.
See [Privacy Policy](PRIVACY.md) and [security/account design](docs/SECURITY.md).
Accounts and automatic diagnostic email are not implemented. Commit, push and installation
of the new candidate still follow HANDOFF's approval rules.
The offline policy, link-ping blocking, private Shields isolation, certificate protections,
bounded updates and stronger masking passed focused checks and native scratch QA.
Full verification: [Privacy/security record](tools/test/PRIVACY-SECURITY-2026-10-21.md).

- **Updates:** click the AB5 badge/build header in Settings to expand all sixteen
  milestones, including their dates and descriptions. Keyboard activation works too.
- **Performance:** expand a metric card, tab or process for more detail. CPU uses the
  actual sample interval; GPU readings cover Aviary's processes and busiest physical
  engine. Missing readings, shared processes and individual lifetime peaks are explained.
- **Storage Usage:** measurements refresh while the page is visible, with 2–10 seconds
  between completed scans according to their cost. Expanded categories retain their state
  and show exact bytes, allocated disk space and file counts. Incomplete scans and scope
  limits are identified. This is a periodic snapshot; files can change during a scan.
- **Size display:** values automatically switch between B, KB, MB, GB and TB using
  decimal units: 1 KB = 1,000 bytes; 1 MB = 1,000,000 bytes. Measurements retain precision
  until display. Performance, Storage and the optional corner readout use matching units.
- **Less background work:** page measurements pause when hidden or left, expanded details
  retain focus, and sorting does not add samples to the graphs.

The ordinary Desktop/taskbar copy in this workspace is `publish/Aviary/Aviary.exe`.
It received the tested Settings update on 21 October at 20:27 +08:00 and was reopened
and visually verified. All 662 installed files match the tested manifest; all 54 local
Collection exhibits are available. The previous installation is preserved under
`.artifact/ab5-published-before-settings-mb-20261021`; the user's profile was preserved.

Local checks include 26 Performance checks, 18 Storage checks, 11 size boundary/conversion
cases and the 99-row Settings audit. Native scratch sessions closed without crashes or
recorded issues. See the [Settings verification record](tools/test/SETTINGS-LIVE-2026-10-21.md)
for evidence and measurement limits. These results are local verification of the follow-up.

## Run

Extract a prepared ZIP and double-click **Aviary.exe** inside its Aviary folder. Keep
all bundled files together. The x64 package includes .NET and Windows App SDK; it also
requires Microsoft Edge WebView2 Runtime, normally present on Windows 11. This is an
unsigned app. No installer, updater or macOS package is shipped yet.

The normal profile lives in `%LOCALAPPDATA%\Aviary`. Test launches must explicitly set
`AVIARY_PROFILE_DIR` to a scratch folder; do not point a test at the normal profile.

## Build and verify

On Windows x64, install the SDK recorded in [global.json](global.json). From this folder:

```powershell
dotnet restore src/Aviary/Aviary.csproj --locked-mode -p:Platform=x64 -p:RuntimeIdentifier=win-x64
dotnet build src/Aviary/Aviary.csproj --no-restore -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64
dotnet run --project tools/test/filters/FilterHarness.csproj -c Release
dotnet run --project tools/test/security/SecurityHarness.csproj -c Release
dotnet run --project tools/test/performance/PerformanceHarness.csproj -c Release
dotnet run --project tools/test/storage/StorageHarness.csproj -c Release
node tools/test/settings-audit.js
node tools/test/m7-script-test.js
node tools/test/collection-availability-test.js
node tools/test/release-state-test.js
./tools/test/library-sync-test.ps1
./tools/release/prepare.ps1 -NoRestore -SourceOnly
```

Node 22+ runs the small JavaScript checks. `prepare.ps1` makes a new folder under
`.artifact`, validates required runtime/data files, and produces a ZIP, SHA-256 checksum
and a manifest of every packaged file. It never replaces `publish/Aviary`. Omit
`-SourceOnly` only for a local package with the existing private Collection payload.
The SDK and NuGet lock file make dependency selection repeatable. Windows App SDK's
exact version is 1.7.260224002, pinned in the project and lock after real CI passed.
The lock was refreshed deliberately without changing resolved dependencies.

## Collection and source-only builds

The Collection shell, catalog and landing pages are source. Grant's personal projects,
thumbnails and game exports under `Library/files`, `thumbs` and `builds` are ignored by
Git and excluded from CI packages. Missing exhibits say **Not in this copy**. A local
package can carry all existing copies; do not upload that package without Grant's approval.

`tools/library/sync.ps1` reads original projects and copies approved files. Its default
preserves old copies when sources are absent or moved. `-SourceRoot` selects their actual
location. Pruning requires explicit `-Prune` and every source pattern to be available;
preflight failure makes no changes. Do not use sync to repair or edit original projects.

## Project status

- [Build/download guide](docs/BUILDING.md)
- [Alpha Build 5 plan](PHASE5-PLAN.md)
- [M7 verification](tools/test/M7-VERIFICATION-2026-10-20.md)
- [M8 verification](tools/test/M8-VERIFICATION-2026-10-20.md)
- [M9 final validation](tools/test/M9-FINAL-2026-10-21.md)
- [Live Settings and size-display verification](tools/test/SETTINGS-LIVE-2026-10-21.md)
- [Continuation checkpoint](AB5-CONTINUE.md)
- [Project handoff and working rules](HANDOFF.md)
- [Test kit](tools/test/README.md)

For a new development chat, read the continuation checkpoint first, then HANDOFF and the
latest verification record. Inspect the working tree before editing and preserve the
uncommitted Settings changes. The local update was explicitly authorized and completed;
new commits/pushes still require approval under the project's existing rules. Keep personal
Collection payloads local. The completed AB5 continuation is paused; an unchanged checkpoint
does not call for another build, test run or installation.

GitHub builds run on pushes to main, pull requests and manual requests. Both the
[first run](https://github.com/GrantWally120/Aviary/actions/runs/39507147323) and the
[pinned SDK run](https://github.com/GrantWally120/Aviary/actions/runs/39508704024) passed.
The [final release-source run](https://github.com/GrantWally120/Aviary/actions/runs/39512372624)
also passed and provides the final source-only Windows ZIP.
Those runs cover the completed AB5 release. The new Performance/Storage harness steps are
prepared in the local workflow and have not yet run remotely for the Settings follow-up.

The source is not currently offered under an open-source licence. Third-party runtimes
keep their upstream terms; bundled filter attribution and licences are in
[Filters/README.txt](src/Aviary/Filters/README.txt). Aviary is independent of Apple,
Brave, Microsoft and the EasyList authors.
