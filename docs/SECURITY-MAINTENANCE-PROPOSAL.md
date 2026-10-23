# Security maintenance proposal — 22 October 2026

Status 23 Oct 2026: DONE, tested, installed and pushed; see
SDK-2.5-MIGRATION-2026-10-23.md. Original status line: prepared for Grant's approval. No dependency changes or publisher-list downloads
have been made for this proposal. The privacy/security implementation and corrected policy
are already installed from clean source 102abec. This proposal does not start Beta Build 1.

## Verified remaining work

The project pins Microsoft.WindowsAppSDK [1.7.260224002] and Microsoft.Graphics.Win2D 1.3.2.
Microsoft lists Windows App SDK 1.7 as out of support, with servicing ended 18 March 2026.
The current stable SDK package is 2.5.1. The NuGet vulnerability query reported no known
vulnerable packages in the current graph; that does not establish support or future patches.

The bundled EasyList/EasyPrivacy snapshots date from September. The scratch Safety Check
reported them 38 days old. Current blocking fixes do not refresh those snapshots. Existing
profile updates, if any, can be newer; do not assume this is the age of Grant's profile copy.

## Proposed changes after approval

| Item | Current | Proposed initial target |
| --- | --- | --- |
| Windows App SDK | [1.7.260224002] | [2.5.1], supported stable |
| Win2D | 1.3.2 | [1.4.0], coordinated with the split WinUI SDK |
| Ads list | Bundled September snapshot | Current EasyList from its existing publisher |
| Tracker list | Bundled September snapshot | Current EasyPrivacy from its existing publisher |

1. Prepare an isolated source checkout and preserve the verified installed copy. Resolve the
   two exact package targets with a fresh lockfile. Inspect the resulting native/runtime graph,
   compatibility and package size. The SDK meta-package includes optional components; assess
   using the supported WinUI/Foundation components needed by Aviary before accepting extra
   runtime surface. No AI, account, widget or search feature is requested by this migration.
2. Build and run existing safety/measurement/Collection checks. Verify unpackaged self-contained
   startup, app-instance redirection, WebView2 regular/private tabs, Classic and Liquid Glass,
   dialogs/menus, downloads, crash handling and normal close on scratch profiles. Keep the
   current .NET/Windows minimum targets unless the supported graph requires an explicit change.
   Compatibility with these targets is not yet tested; do not claim the upgrade is ready to install.
3. Fetch only the already-approved publisher endpoints below, with no cookies/credentials,
   HTTPS validation, bounded bodies and provenance/hashes. Preserve unmodified list text and
   attribution, and verify parsing, targeted exceptions, important rules and representative
   native blocking/compatibility. Keep cookie/social lists and auto-update choices unchanged.
4. Present the tested source/lockfile changes, package size and native evidence before any
   new installed-copy replacement or publication. Keep all personal Collection payloads local.

Approved-publisher download targets proposed for this refresh:

- https://easylist.to/easylist/easylist.txt
- https://easylist.to/easylist/easyprivacy.txt

The newer Win2D package references Microsoft.WindowsAppSDK.WinUI >= 1.8.260204000;
the newer SDK meta-package uses split component dependencies. This is a dependency migration,
not a patch-version substitution into the existing 1.7 graph. Abort the candidate if restore
or native verification fails; retain the working copy and report the exact problem.

## Why approval is required

HANDOFF section 2 says **"Ask before adding a NuGet package."** Its standing orders also
require approval before downloads and new filter lists. A supported SDK migration introduces
split NuGet dependencies, and the list refresh contacts the publishers. The broad bug-review
request has not been treated as specific approval for those additions/downloads.

No work should run automatically. Grant's current cutoff is 02:00 Manila on 22 October.
Any later work needs a new human instruction; the completed AB5 automation stays paused.

## Primary references checked

- [Microsoft servicing policy](https://learn.microsoft.com/windows/apps/windows-app-sdk/release-channels)
- [Windows App SDK 2.5.1 package and dependency metadata](https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.5.1)
- [Win2D 1.4.0 package and dependency metadata](https://www.nuget.org/packages/Microsoft.Graphics.Win2D/1.4.0)

Current-graph audit evidence is local at
`.artifact/ab5-privacy-security-policy-local-20261021/dependency-audit.json`.
