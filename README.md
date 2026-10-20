# Aviary

A native Windows browser built with WinUI 3 and Microsoft Edge WebView2. Alpha Build 5
is **The Long Run**, version 0.5.0. It remains a pre-release while M9's first GitHub CI
run and final release are pending. macOS support is deferred.

Classic and Liquid Glass looks, tabs and tab groups, Bird News, Shields, offline
Collection exhibits, and optional Quik prepared tabs are implemented. Quik is in Beta;
its spare tabs use additional memory. Per-tab audio controls and loading indicators
arrive in M8. Measurements and limitations are recorded in the verification notes.

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
node tools/test/settings-audit.js
node tools/test/m7-script-test.js
node tools/test/collection-availability-test.js
./tools/test/library-sync-test.ps1
./tools/release/prepare.ps1 -NoRestore -SourceOnly
```

Node 22+ runs the small JavaScript checks. `prepare.ps1` makes a new folder under
`.artifact`, validates required runtime/data files, and produces a ZIP, SHA-256 checksum
and a manifest of every packaged file. It never replaces `publish/Aviary`. Omit
`-SourceOnly` only for a local package with the existing private Collection payload.
The SDK and NuGet lock file make dependency selection repeatable. Windows App SDK's
resolved version is 1.7.260224002; its project reference will be pinned after real CI
passes, as required by the phase plan.

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
- [M9 release evidence](tools/test/M9-RELEASE-2026-10-21.md)
- [Continuation checkpoint](AB5-CONTINUE.md)
- [Test kit](tools/test/README.md)

GitHub builds are configured for pushes to main, pull requests and manual runs. No
remote CI result is claimed until a repository is connected and its first build passes.
The source is not currently offered under an open-source licence. Third-party runtimes
keep their upstream terms; bundled filter attribution and licences are in
[Filters/README.txt](src/Aviary/Filters/README.txt). Aviary is independent of Apple,
Brave, Microsoft and the EasyList authors.
