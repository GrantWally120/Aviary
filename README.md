# Aviary

A native Windows browser built with WinUI 3 and Microsoft Edge WebView2. **Beta Prep 1**
(version 0.5.1, made 25 October 2026) is the current update; it replaces Alpha Build 5 as the build
you are on and gathers everything done from 21 to 24 October (see
[docs/BETA-PREP-1-2026-10-24.md](docs/BETA-PREP-1-2026-10-24.md)). It is installed locally and pushed.
Alpha Build 5 was **The Long Run**, version 0.5.0, with all sixteen milestones complete, finished on
Wednesday 21 October 2026. The AB5
release and the Settings/privacy/security follow-ups passed Windows CI. The latest
tested privacy/security copy, with the 22 October review fixes, is installed locally; the
privacy/security source and the fixes are both pushed (Windows CI run #10 on 9c759f0 passed).
On 23 October the Prism 2 look fixes, the account groundwork, Windows App SDK 2.5.1, Win2D 1.4.0 and
fresh EasyList/EasyPrivacy lists were installed and pushed (Windows CI run #12 on e55cb6d passed).
macOS support and starting Beta Build 1 remain deferred.

## What is next

- **Artemis** is on hold: Grant has his own plan for it. The Prism 2 spec (`Prism2-Specimen.html`, one folder above this one,
  generated from `design/prism2.tokens.json`) is there for when he wants it; `artemis.html` is left alone.
  See [the Prism 2 report](docs/PRISM2-AND-ACCOUNTS-PREP-2026-10-23.md).
- **The accounts system** is not started and waits on decisions (purpose, operator, name, private contact, where
  the data lives, a Google sign-in client). The groundwork is in and connected to nothing; see
  [docs/SECURITY.md](docs/SECURITY.md). The assistant leads this one and explains it in plain language.
- **Ad-blocker lists** are fresh as of 23 October but only stay fresh if **Settings → Shields → Keep the lists up to date**
  is set to Daily or Weekly (the default is Off).

## Current app and USB backup — 22 October 2026

Open **Aviary.lnk** in this folder, or run `publish/Aviary/Aviary.exe`.
This is Beta Prep 1, version 0.5.1 (25 October 2026), on top of the verified Alpha Build 5 (0.5.0, 16/16, finished
Wednesday 21 October 2026), including the Settings and privacy/security updates and the 22 October review fixes
(YouTube playback, everyday keyboard shortcuts, new-tab focus). Desktop/taskbar shortcuts use the same installation.

Grant authorized cleaning this E: workspace. Redundant app distributions, old
release ZIPs, rebuildable outputs and scratch test profiles were removed:
140,278 files, about 19.6 GB of logical file data. Two exact recovery ZIPs are in
**Backup**, with manifests, checksums and restore instructions: the current one
(`Aviary-BP1-local-win-x64.zip`, 25 October) and the previous build as a rollback
(`Aviary-AB5-sdk25-parallax-local-win-x64.zip`); older ones are kept. `Backup/README.txt` says which is which.
Copy that folder to USB for the app backup; copy the whole cleaned Zoo folder,
including hidden `.git`, to retain development sources/history too.
The backup contains private Collection projects; keep it local.
The normal browser profile is separate and was left untouched.

Current Collection files and source backups were preserved. Three unique older
Collection files are retained in `Backup/Collection-history`, with an origin index.
Scratch logs/configuration/fixtures are compressed under
`.artifact/cleanup-20261022`; existing screenshots and release receipts remain.
Earlier package/rollback paths elsewhere in these documents are historical:
their redundant applications/ZIPs were removed. Use the current recovery ZIP.
See [cleanup verification](tools/test/CLEANUP-2026-10-22.md). No external drive
was accessed or written, and no app rebuild/reinstallation was needed.

Classic and Liquid Glass looks, tabs and tab groups, Bird News, Shields, offline
Collection exhibits, and optional Quik prepared tabs are implemented. Quik is in Beta;
its spare tabs use additional memory. Per-tab audio controls and loading indicators
arrived in M8. Measurements and limitations are recorded in the verification notes.

## Latest local update — 21 October 2026

Privacy/security follow-up is implemented, verified and installed. The final policy
clarification was applied on 22 October at 00:07 +08:00.
See [Privacy Policy](PRIVACY.md) and [security/account design](docs/SECURITY.md).
Accounts and automatic diagnostic email are not implemented. Grant authorized finishing the
prepared update and continuing the review, with a hard stop before 02:00 Manila on 22 October.
The offline policy, link-ping blocking, private Shields isolation, certificate protections,
bounded updates and stronger masking passed focused checks and native scratch QA.
The continued review also repaired icon redirect/filter bypasses, unbounded/stalled background
downloads, async script ownership, message bounds, additional JSON redaction gaps and legacy
Windows memory availability. Final source 102abec passed [actual Windows CI](https://github.com/GrantWally120/Aviary/actions/runs/39630732602),
including 112 security checks, 30 Performance checks and complete source-only packaging.
Full verification: [continued security audit](tools/test/PRIVACY-SECURITY-AUDIT-2026-10-21.md).
The [initial privacy/security record](tools/test/PRIVACY-SECURITY-2026-10-21.md) preserves earlier QA.
The policy also explains local form-text inspection for error masking and its limits.
Remaining dependency/filter maintenance is prepared for approval in the
[security maintenance proposal](docs/SECURITY-MAINTENANCE-PROPOSAL.md): the pinned Windows App
SDK 1.7 is out of support, although the current NuGet audit reports no known vulnerable packages.

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
It received the final combined Settings/privacy/security copy on 22 October at 00:07 +08:00,
and the review-fixes build on the evening of 22 October.
All 663 installed files match the tested manifest (`Backup/manifest-fixes.json`); all 54 local
Collection exhibits are available. The current recovery package is
`Backup/Aviary-AB5-fixes-local-win-x64.zip`; the rollback is `Backup/Aviary-AB5-local-win-x64.zip`
(102abec, without the fixes). The redundant prior installations were
removed during the authorized 22 October cleanup. The user's profile was preserved.

The current Settings audit covers 100 live rows with none unread and three deferred rows.
The original [Settings verification record](tools/test/SETTINGS-LIVE-2026-10-21.md) retains
measurement details and limitations. Native privacy/security QA confirms blocked icon redirects,
tracking pings, ordinary request compatibility, private storage isolation and the offline policy.

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
node tools/test/diagnostics-script-test.js
node tools/test/collection-availability-test.js
node tools/test/release-state-test.js
./tools/test/library-sync-test.ps1
./tools/release/prepare.ps1 -NoRestore -SourceOnly
```

Node 22+ runs the small JavaScript checks. `prepare.ps1` makes a new folder under
`.artifact`, validates required runtime/data files, and produces a ZIP, SHA-256 checksum
and a manifest of every packaged file. It never replaces `publish/Aviary`. Omit
`-SourceOnly` only for a local package with the existing private Collection payload.
The SDK and NuGet lock file make dependency selection repeatable. Windows App SDK 2.5.1 is
referenced as its WinUI 2.3.9, Foundation 2.3.12, InteractiveExperiences 2.1.9 and Base 2.0.4
components, with Win2D 1.4.0, pinned in the project and lock (docs/SDK-2.5-MIGRATION-2026-10-23.md).
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
- [Prism 2 fixes and account groundwork, 23 Oct](docs/PRISM2-AND-ACCOUNTS-PREP-2026-10-23.md)
- [Windows App SDK 2.5.1, Win2D 1.4.0 and the list refresh, 23 Oct](docs/SDK-2.5-MIGRATION-2026-10-23.md)
- [Privacy and security before accounts](docs/SECURITY.md)
- [Test kit](tools/test/README.md)
- [Review of the AB5 work and the fixes that followed, 22 Oct](AVIARY-REVIEW-2026-10-22.md) (and the earlier [playtest review, 23 Sep](AVIARY-REVIEW-2026-09-23.md))

For a new development chat, read the continuation checkpoint first, then HANDOFF and the
latest verification record. Inspect the working tree before editing: the Settings and privacy/
security work is committed and pushed (d41182f), and only documentation, the test-kit launcher
repairs and the cleanup notes are local. The local update was explicitly authorized and completed;
new commits/pushes still require approval under the project's existing rules. Keep personal
Collection payloads local. The completed AB5 continuation is paused; an unchanged checkpoint
does not call for another build, test run or installation.

GitHub builds run on pushes to main, pull requests and manual requests. Both the
[first run](https://github.com/GrantWally120/Aviary/actions/runs/39507147323) and the
[pinned SDK run](https://github.com/GrantWally120/Aviary/actions/runs/39508704024) passed.
The [final release-source run](https://github.com/GrantWally120/Aviary/actions/runs/39512372624)
also passed and provides the final source-only Windows ZIP.
Those runs cover the completed AB5 release. The Performance and Storage harness steps, added for
the Settings follow-up, have since run remotely: [run 39630732602](https://github.com/GrantWally120/Aviary/actions/runs/39630732602)
on 102abec passed with the security, Performance and Storage checks and the source-only package.

The source is not currently offered under an open-source licence. Third-party runtimes
keep their upstream terms; bundled filter attribution and licences are in
[Filters/README.txt](src/Aviary/Filters/README.txt). Aviary is independent of Apple,
Brave, Microsoft and the EasyList authors.
