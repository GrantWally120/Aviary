# Windows App SDK 2.5.1, Win2D 1.4.0 and the filter-list refresh

23 October 2026. Done in an isolated worktree on the branch `sdk-2.5-migration`, from `142a8d3`, following
[the maintenance proposal](SECURITY-MAINTENANCE-PROPOSAL.md). Grant's instruction was "you choose what's best" after
being told what the two items were and that the project rule is to ask first; the proposal's own gate still applies:
nothing is installed or pushed until Grant has seen this and said yes.

## What changed

| Item | Before | After |
|---|---|---|
| Windows App SDK | `Microsoft.WindowsAppSDK` [1.7.260224002] (out of support since 18 Mar 2026) | four components: WinUI [2.3.9], Foundation [2.3.12], InteractiveExperiences [2.1.9], Base [2.0.4] (what the 2.5.1 meta-package resolves) |
| Win2D | 1.3.2 | 1.4.0 |
| WebView2 SDK (transitive) | 1.0.2903.40 | 1.0.3719.77 |
| Windows SDK BuildTools (transitive) | 10.0.22621.756 | 10.0.26100.4654 (+ BuildTools.MSIX 1.7.251221100) |
| `tools/release/verify.ps1` | requires `resources.pri` | requires `Aviary.pri` (SDK 2.x names the resource index after the app) |
| EasyList | 202609130550, 13 Sep (79,927 rules) | 202610231316, 23 Oct (85,208 rules) |
| EasyPrivacy | 202609130541, 13 Sep (56,063 rules) | 202610231316, 23 Oct (56,399 rules) |

Source code: **no C# change was needed**; the app compiled against SDK 2.5.1 with 0 errors and 0 warnings.

### Why the four components and not the meta-package

The meta-package build also compiled and ran, but it adds the AI, ML, Widgets, Search and DWrite components: 534 files and
236.5 MB against 466 files and 176.9 MB, including about 70 `Microsoft.Windows.AI.*`, `Widgets`, `Search` and
`SemanticSearch` files a browser shell does not use. The proposal asked to assess that before accepting the extra runtime
surface, so the lock file carries only what Aviary needs. For comparison the current install is 509 files and 180.9 MB
(without the private Collection). Both builds bundle the same .NET 9.0.19.

### Downloads

From nuget.org only, into the NuGet cache. Roughly 550 MB of packages landed there today, including components of the first,
meta-package restore that the final lock does not use; the app itself ships none of them. From `https://easylist.to/easylist/easylist.txt`
and `.../easyprivacy.txt` only, HTTPS, no redirects, no cookies, checked as strict UTF-8, under 40 MB, with the right header,
a newer version and the licence text, then copied byte for byte (sha256 `3d7015a9...9393` and `819a6112...799f`). The
Cookie and Social lists were not touched.

## What was run (candidate: source-only, 467 files, stamp `0.5.0+142a8d3`)

| Check | New build | Installed build (SDK 1.7), same test |
|---|---|---|
| Compile | 0 errors, 0 warnings | |
| `prepare.ps1` / `verify.ps1`, ZIP checksum | 467 files verified | |
| Filter, security, performance, storage harnesses | 125, 317, 30, 18 passed | same code |
| Node checks (settings, m7, diagnostics, collection, release state, Prism 2 tokens) | all pass | |
| `publish-smoke.ps1` | 17 of 18; the miss is the Collection files, absent in a source-only build | 18 of 18 (full build) |
| Live CSP in the running app | 33 of 33 | 33 of 33 |
| Tab-strip overlap | passes | passes |
| Links test | 27 of 27 | |
| Tab groups test | 39 of 40 (once 30 of 40, see below) | 39 of 40 |
| Downloads test | 53 of 53 (once 51 of 53, see below) | 53 of 53 |
| Window hit-test of the caption buttons | `Close -> HTCLOSE` | identical |
| Native chrome in Liquid Glass dark (toolbar and tabs, top 300 px) | pixel-identical | |
| Classic light/dark, Liquid Glass light/dark | start normally; screenshots checked by eye | |
| Where the WinUI, Win2D and WebView2 libraries load from | all from the app's own folder (71 modules), same as before | |

The two "once" results were the first run of each test on the new build, made right after an interrupted test batch on the
same machine while Aviary was also open; the same tests on the same build passed on the next run, and the installed build
passes them. The one tab-groups failure that is steady (the sidebar Tab Groups pane in the Top bar layout) is identical on the
installed build. I treat the transient ones as test-environment noise, not as a defect I can demonstrate, and say so plainly.

## Not verified

- **The hidden-WebView bug** (microsoft-ui-xaml #6256: a WebView created while collapsed can render but ignore the mouse),
  which `NOTES-webview2-architecture.md` says to re-test on every SDK bump. It needs a real mouse click on a background tab,
  which the test kit may only send when the desktop guard says nobody is using the machine, and Grant was working with me at the time, so I did not try. After installing, click a
  background tab once and click something on its page.
- The tests that use real desktop input (`native-crash`, `si-test` single-instance, `tabmenu`, `experiments`, `forget`,
  `startbg`) were not run for the same reason. Every Windows App SDK runtime from 1.2 to 2.5.1 is installed on this PC, so
  start-up on a PC without them is shown by the module check above (the libraries come from Aviary's folder), not by a clean machine.
- A full build with the private Collection and its 18-check smoke test on the installed copy is the step after approval.

## Rollback

The current install is unchanged and still the CURRENT/ROLLBACK pair in `Backup/` (see `Backup/README.txt`). If the new
build misbehaves, put the old folder back; the branch can be dropped without touching `main`.
