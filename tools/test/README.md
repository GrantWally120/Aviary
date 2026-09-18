# Aviary test kit

How Aviary gets tested in the real app without touching Grant's profile or fighting him for the
mouse. Built during Phase 5 M2 (12 Sep 2026); HANDOFF §5 explains why each piece exists. Read
the **container** box there first: every tool here runs inside the Claude app's MSIX container,
where `%LOCALAPPDATA%` is virtualized, so tests must use a scratch profile.

## Build a test copy

```
dotnet publish src/Aviary/Aviary.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 ^
  -p:SelfContained=true -p:WindowsAppSDKSelfContained=true -p:WindowsPackageType=None ^
  -p:PublishSingleFile=false -p:PublishTrimmed=false -p:PublishReadyToRun=false ^
  "-p:PublishDir=C:/Users/user/Documents/Claude CoWork Enclosed/Zoo/tools/test/pub/"
```

Never publish over `publish/Aviary` while Grant's copy is running from it. From PowerShell use
forward slashes in PublishDir: a trailing `\"` swallows the rest of the arguments.

## The pieces

| File | What it does |
|---|---|
| `launch-m2.ps1 -Profile name [-Fresh] [-SettingsJson '{…}']` | Launches `pub/Aviary.exe` with `AVIARY_PROFILE_DIR=profiles/<name>` and `AVIARY_TEST_BROWSER_ARGS=--remote-debugging-port=9222`. Seed `{"improvementCollection":"true"}` to read its diagnostics. |
| `cdp.js` | DevTools driver: `list`, `eval <tab> "<js>"`, `nav <tab> <url>`, `click <tab> "<css>"` (trusted), `type <tab> "<css>" "<text>"` (trusted), `key <tab> "ctrl+shift+KeyN"` (trusted, reaches Aviary's shortcut script; `ctrl+Comma` opens Settings), `wait`, `shot <tab> <out.png> [WxH] [light\|dark]` (a page at a set size and scheme, no window touched — it undoes the override in the same session, because WebView2 keeps it after the session ends). `<tab>` = target-id prefix, url/title substring, or `#n`. DevTools navigations skip Aviary's address bar — test that path with one guarded keystroke. |
| `package.json` | Only here so Node stops before `C:\Users\user\Documents\package.json` (an old, malformed Wally2TTD file that breaks every `node` run under Documents). |
| `m2site.js` | Two sites on loopback, port 8766: A = 127.0.0.1, B = 127.0.0.2 (different hosts, so cross-site). /echo, /3pc-top, /ref-top, /params, /form, /webrtc, /trackers, /tracker-frame-top, /login, /fullscreen, /favicon-proto, /download, /spoof-shortcut, and /hits (every request with its Referer, Cookie, Sec-GPC, Sec-CH-UA). |
| `testsite.js` | M1's secrets site (port 8765): planted passwords, tokens, cookies, card numbers — grep the diagnostics folder for them afterwards; the answer must be zero. |
| `guard.ps1` | Exit 3 if someone else touched the keyboard or mouse in the last 15 s. Run before ANY GUI input. (Until 13 Sep a `lastinput.txt` stamp from before a restart made it count every input as ours — it waved everything through.) |
| `front-and-shoot.ps1`, `click.ps1`, `type.ps1`, `type-hwnd.ps1` | Foreground-guarded input and capture (they refuse to send anything unless the right window is in front). |
| `peek.ps1`, `rect.ps1` | Capture / measure the window WITHOUT bringing it forward (PrintWindow). |
| `min-window.ps1` | Bug 50's repro: minimise N ms after the window appears, report alive/dead. Each profile's `crash.log` now says why it died (NativeCrash.cs); set `AVIARY_TEST_NATIVE_DETAIL=1` first to add the thrown object's first 128 bytes. |
| `native-crash.ps1 [-Mode native\|ui\|fatal] [-Profile name]` | Proves the crash recorders: launches with `AVIARY_CRASH_TEST=<mode>` on a fresh scratch profile (collection on), parks the window at the back without activating it, waits for the end and prints `crash.log` and `crashes/`. `native` = a real C `abort()` on the UI thread — the way bug 50 ends Aviary. |
| `dbwin.ps1 [-Seconds n] [-Name Aviary] [-Out file]` | Listens to OutputDebugString (DebugView's DBWIN channel), keeping one process's lines — nothing to install. WinUI stayed silent without a debugger (14 Sep), but other components may not. |
| `si-test.ps1 [-MinimiseFirst] [-CloseFirst]` | Bug 51: launch again on the profile a test instance has open. The new process must end itself (~150 ms) and the running window come to the front, restored. |
| `si-race.ps1`, `si-race2.ps1` | Bug 51's shutdown race, both orderings (close-then-launch, launch-then-close) over a sweep of delays. Every trial: never two Aviarys on one profile, no unclean-exit, no lost launch. |
| `wipe-probe.js <origin>…` | Bug 52: plants cookies for seven sites, calls Storage.clearDataForOrigin on each origin given, reports what it took. `TYPES=…` to pass storage kinds. Run against a scratch instance only. |
| `edit.js` | Scripted source edits that respect each file's line endings (the tree mixes CRLF and LF). |
| `m3site.js` | M3's ad-blocker site on port 8771. Launch with `-ExtraArgs '--host-resolver-rules="MAP * 127.0.0.1:8771, EXCLUDE localhost"'` and `{"httpsOnly":"false"}` so every host lands here: news.test loads real ad and tracker addresses (answered locally), plus pages for a site's own rule, an exception, `$generichide`, `$document` and a plain-host site. `/hits` lists what got through — a blocked request is one that is NOT there. `/lists/` serves the lists re-dated for "Update now" (`AVIARY_TEST_FILTER_BASE=http://127.0.0.1:8771/lists/`). HANDOFF §5 has the gotchas (HSTS-preloaded hosts, ports). |
| `uia.ps1 -ProcessId n (-Name text \| -Id xName) [-Action Invoke\|Toggle\|Dump\|Help\|SetValue\|Value] [-Value text] [-Window Private]` | Presses XAML controls through UI Automation — no mouse, no keyboard, no focus change, so it is safe while Grant is at the PC. Flyouts are searched too. `Invoke` on a label climbs to the button holding it (a Smart Search row). `SetValue` sets a text box's text like typing (M4: `cdp.js key <tab> "ctrl+KeyL"` first, so the address bar has focus); `Value` reads it back with its focus. |
| `press-hold.ps1 -ProcessId n -Id xName -Out shots/x.png [-HoldMs 250]` | Holds the left button down on one control and captures it hovered and pressed (400% zoom), printing the width of the filled square in each — how Liquid Glass's 94% press dip was measured (27.5 → 26 px). Guarded: refuses unless the test window is in front and nobody has touched the machine for 15 s; moves with `mouse_event` (SetCursorPos never reaches WinUI's hover); puts the cursor back. |
| `search/` | M4's search harness: `dotnet run -c Release` there (`offline` to skip the network). Compiles the shipping `SearchEngines.cs` + `SearchSuggestions.cs`; 57 checks of what may be sent and how answers are read, then two real requests to every engine that suggests, through the app's own client. |
| `filters/` | The ad blocker's engine harness: `dotnet run -c Release` in that folder. Compiles the shipping `FilterEngine*.cs`; 101 checks plus real-list build time, skip report and a 200,000-request cost run. |
| `bgsite.js` | Bug 61's page-background site on port 8772, two sites (127.0.0.1 / 127.0.0.2): `/plain` (no styles), `/empty`, `/text` (text/plain), `/white`, `/black`, `/scheme-dark`, `/scheme-auto`, `/prefers`, `/slowcss?ms=N&bg=RRGGBB` (a render-blocking stylesheet held back N ms), `/slowdoc?ms=N&bg=…` (the server waits N ms), `/links` (every page same-site, cross-site and `target=_blank`). The right half of every page is free of text, for sampling. |
| `flash-watch.ps1 [-Then 'cmd …'] [-Ms n] [-At n] [-Resize WxH] [-Snap out.png] [-Title Private]` | What the window shows WHILE a page is replaced: PrintWindow as fast as it goes (~7 ms a capture at 1280x800) and every change of colour at three points on the page, with `-Then` fired at `-At` ms. `-Resize` restores (even from minimised), sizes and parks the window at the BOTTOM of the z-order without activating it — behind Grant's windows. `-Snap` = one capture. Finds the process's largest visible window: `MainWindowHandle` can name a 160x28 helper. No input, no focus change. |
| `bg-battery.ps1 -Label name [-Only 1,5] [-Repeat n]` | 26 cases through `flash-watch.ps1` — same-/cross-site, slow CSS, slow server, link clicks, Start Page ↔ website, Settings and Ctrl+T in new tabs, links opening new tabs (plain, dark, slow, empty, text), tab switches (including a tab loaded while hidden). Writes `shots/bg-<label>.txt`. Needs `bgsite.js` and a test instance. |

Output (`pub/`, `profiles/`, `shots/`, `pid.txt`, `lastinput.txt`, `filters/bin`, `filters/obj`) is ignored by git.
