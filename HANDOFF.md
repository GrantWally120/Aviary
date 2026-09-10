# Aviary — project handoff

Everything a new session needs to pick this up. Written 6 September 2026 at the end of
Phase 3; updated 7 September 2026 at the end of Phase 4.

---

## 1. What this is

**"The Zoo"** is Grant's project name. **Aviary** is the browser's product name — Grant is
the creative director, doesn't write code, and judges on appearance. If he'd rather the app
itself be called *Zoo*, it's a find-and-replace, not a rewrite.

A **native Windows 11 desktop web browser that wears Apple Safari's design language**.
Not Electron, not a skin — a real WinUI 3 app.

- **Repo:** `Zoo/` (git repo, 10 commits, branch `main`, no remote yet)
- **App:** `Zoo/publish/Aviary/Aviary.exe` — **there is a shortcut on Grant's Desktop**
- **Landing page:** `Zoo/Aviary.html` — a living document, updated every phase
- **Phase 0 mockup:** `Zoo/Aviary-Phase0.html`

### Hard requirements (from the original brief)
1. Mica backdrop, extended title bar, native Windows 11 caption buttons, rounded corners
2. Ship Safari's user agent so sites serve Safari code paths
3. Two themes switchable at runtime — **Liquid Glass** (Safari 26/Tahoe) and **Classic**
   (Safari 15–18), every colour/radius/blur in tokens, zero hardcoded colours in controls
4. Two tab layouts — top bar and vertical sidebar (**sidebar not built yet**)
5. Light and dark following the system
6. A GitHub Actions workflow producing a downloadable .exe (**written, never run — no repo**)

### Out of scope for v1
Real WebKit · extensions · account sync · bundled Widevine · mobile · macOS/Linux

---

## 2. Working agreements with Grant

- **Never leave him at a terminal.** Anything he runs is a double-click or a download.
- **Update `Aviary.html` at the end of every phase.** The whole roadmap is a single
  `PHASES` array near the bottom of its `<script>` — edit that array, nothing else.
- **Do NOT publish these as Artifacts.** He said so explicitly on 2026-09-06; keep them as
  plain local HTML. Two stale artifacts exist from before that instruction — ignore them.
- **Trust his reported symptom over your own tests.** This has already bitten once: he
  reported dropdowns that wouldn't close, and the same symptom was visible in a screenshot
  that had been explained away as test noise.
- **Ask before adding a NuGet package.**
- **Features roll out 25% / 25% / 50%** across Phases 3, 4 and 5.

---

## 3. Phase plan

Phases **00–05 are ALPHA**; **06 and up are BETA**.

| Phase | Track | Name | State |
|-------|-------|------|-------|
| 00 | alpha | Interface study | done 4 Sep 2026 |
| 01 | alpha | The shell | done 6 Sep 2026 |
| 02 | alpha | Tabs and settings | done 6 Sep 2026 |
| 03 | alpha | Theming and features | done 6 Sep 2026 |
| 04 | alpha | Safari features | done 7 Sep 2026 |
| **05** | alpha | **Alpha Build 5 (pre-release)** | **NEXT** |
| 06 | beta | Beta Build 1 — "Testing 1" | planned |
| 07 | beta | Beta Build 2 — "Testing 2" | planned |

**Phase 04 shipped 7 Sep 2026.** The Start Page, the sidebar (requirement 4 — the last
unmet hard requirement), Reader + the "Aa" menu, real favicons, history, reading list,
the Smart Search Field, the Privacy Report, pinned tabs, our own context menus, page
zoom, and reload-becomes-stop. Two planned items moved to Phase 05 rather than shipping
half-built: the **download progress ring** and **tab groups**.

**Phase 05** — "Alpha Build 5", the last alpha and the widest. Carries an **Experimental
menu inside Settings**: opt-in flags each named and explained in plain English. First one
is a **frame-rate ceiling** (hold 60 / unleashed 120+ / let the display decide); also
planned: reduced compositing, tab suspension. By Phase 5 **all privacy features must
actually work**, plus more security/privacy features and more safe search engines.
Grant's stated goal: *"my own browser… mainly just me… for it to be safe and private."*

**Phase 06** — Beta Build 1: real profile data, session restore, crash reporting,
performance pass, installer, daily-driver testing.

---

## 4. Environment (verified on Grant's machine)

- **.NET SDK 9.0.317** is installed (via winget, Grant approved). Build locally.
- **No Visual Studio.** No GitHub repo, no `gh` CLI. Git Credential Manager is present but
  has no stored GitHub login.
- **WebView2 Evergreen 152.0.4191.62** present.
- Fonts: **Segoe UI Variable** and **Segoe Fluent Icons** confirmed installed; Consolas yes,
  Cascadia no.
- Screen 2560×1440 at 100% scaling.

### Build and run
```
cd "C:/Users/user/Documents/Claude CoWork Enclosed/Zoo"
export PATH="$PATH:/c/Program Files/dotnet"
dotnet build src/Aviary/Aviary.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```
Publish (what the desktop shortcut points at):
```
dotnet publish src/Aviary/Aviary.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 \
  -p:SelfContained=true -p:WindowsAppSDKSelfContained=true -p:WindowsPackageType=None \
  -p:PublishSingleFile=false -p:PublishTrimmed=false -p:PublishReadyToRun=false \
  -p:PublishDir="C:/Users/user/Documents/Claude CoWork Enclosed/Zoo/publish/Aviary/"
```
Output is ~172 MB / ~400 files because .NET and WinUI travel with it — that is the price of
"the user installs nothing".

**`<EnableMsixTooling>true</EnableMsixTooling>` is mandatory** even though we ship
unpackaged. Without it, WindowsAppSDK's PRI generation looks for `ExpandPriContent` in a
Visual Studio folder that a bare .NET SDK does not have, and the build dies with MSB4062.

Windows App SDK resolves to **1.7.260224002**. The reference floats as `1.7.*`; pin it once
CI is green.

---

## 5. How to verify changes (this matters — read it)

Grant's rule is *verify by playing*. Green builds have hidden real defects here repeatedly.

**Launch and screenshot:**
```powershell
$p = Start-Process -FilePath "...\Zoo\publish\Aviary\Aviary.exe" -PassThru
# then System.Drawing CopyFromScreen over GetWindowRect($p.MainWindowHandle)
```

**Hard-won lessons about driving the app:**

- **Always check `GetForegroundWindow()` equals Aviary's handle before sending any input.**
  Keystrokes once leaked into Grant's chat window and posted two stray messages.
- **`SetCursorPos` does NOT drive WinUI's pointer pipeline.** Clicks work; *drags do not*.
  Use `mouse_event` with `MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE` (0x8001) and normalised
  0–65535 coordinates. A drag test failed twice before this was the answer.
- Maximise the window (`ShowWindow(h, 3)`) so clicks cannot land on another window.
- Foreground stealing is common on this machine. If fronting fails, **abort rather than
  click blind.**
- **DO NOT inspect `%LOCALAPPDATA%\Aviary\*.json` with `python`.** On this machine `python`
  resolves to the WindowsApps AppX alias (PythonSoftwareFoundation.PythonManager), which runs
  in a container with LocalAppData **virtualized** — it reads a stale shadow copy and reports
  a different size and mtime from the real file. This cost real time on 7 Sep: Python said
  settings.json held 12 keys while `cat` and `wc -c` correctly showed 38. Every store the
  browser writes lives in that folder, so use `cat` / `wc` / PowerShell `Get-Content` for
  them, and keep Python for reading the SOURCE tree, which is not virtualized.

- For pure HTML/CSS/JS bugs in the internal pages, **extract the page and test it in the
  browser pane instead** — far more reliable than driving the app. This is how the dropdown
  fix was verified.

---

## 6. Architecture

```
Zoo/
  Aviary.sln, global.json, .gitattributes, .gitignore
  Aviary.html                     living landing page + roadmap
  Aviary-Phase0.html              the four-combination mockup
  NOTES-webview2-architecture.md  what to keep/change from Microsoft's sample
  HANDOFF.md                      this file
  docs/BUILDING.md                no-terminal guide for Grant
  .github/workflows/build.yml     CI (never run)
  src/Aviary/
    App.xaml(.cs)             entry point, unhandled-exception logging to crash.log
    MainWindow.xaml(.cs)      all chrome + tab management  (~1200 lines, the big one)
    BrowserTab.cs             one tab = one WebView2 + its events
    WebViewEnvironment.cs     THE shared CoreWebView2Environment
    AppSettings.cs            settings.json, flat string map
    Bookmarks.cs              bookmarks.json
    TrackerBlocker.cs         ~120-domain ad/tracker blocklist
    FindInPage.cs             find engine (injected JS) + host helpers
    DownloadItem.cs           download model + session list
    InternalPages.cs          Start Page and Settings, as HTML strings
    Themes/Classic.xaml       token dictionary — Safari 15–18
    Themes/LiquidGlass.xaml   token dictionary — Safari 26
    app.manifest              PerMonitorV2 DPI
```

### Key decisions and why

**One WebView2 per tab, ONE shared environment.** Per-tab WebViews give each tab its own
history and crash isolation. Sharing one environment is what stops each tab spawning its own
browser/GPU/audio processes — get that wrong and the app is unusable at ~15 tabs.

**User data folder is explicit:** `%LOCALAPPDATA%\Aviary\WebView2`. The default sits next to
the .exe and fails with Access Denied anywhere protected.

**Settings is HTML, not XAML.** Every real browser does this (`chrome://`, `edge://`).
Settings is **data-driven** — a `SECTIONS` array in `InternalPages.cs`. Phase 5's
Experimental menu is one more entry in that array, not a rewrite. Saved values are injected
as JSON at the `/*__AVIARY_SETTINGS__*/` marker before navigation.

**The theme token contract.** `Classic.xaml` and `LiquidGlass.xaml` declare an *identical*
key set (`Av*Brush`, `Av*Radius`, …) with Light / Dark / **HighContrast** theme
dictionaries. **If you add a key to one, add it to all three dictionaries in both files.**

**Swapping looks at runtime is a two-part trick:**
1. `ThemeResource` references only re-resolve when an element's theme changes — so
   `RootGrid.RequestedTheme` is nudged and put back.
2. `StaticResource` references (all geometry) resolve **once at load and never again** — so
   every differing radius/margin is re-applied by hand in `ApplyLookGeometry`.

**Keyboard shortcuts need two paths.** XAML `KeyboardAccelerator`s never fire while a
WebView has focus, because WebView2 hosts a child window that takes keyboard input first.
So shortcuts are *also* caught by a capture-phase listener injected into every document
(`ShortcutScript` in `BrowserTab.cs`) which posts them to the host. Ctrl+T/W/L/Tab/F/D.

**Find in page uses the CSS Custom Highlight API**, not `<mark>` wrapping. Wrapping mutates
the page DOM, fires the site's observers and breaks modern sites. `CSS.highlights` paints
ranges without touching the document. Capped at 2000 matches.

**Tab drag** moves buttons between grid columns rather than rebuilding the strip —
rebuilding destroys the element holding the pointer capture. Handlers **must** use
`AddHandler(..., handledEventsToo: true)` because ButtonBase marks pointer events handled.

---

## 7. What works today

**Read the verification split at the end of this section before trusting any of it.** The
Phase 1-3 heading here used to say "all verified by running it"; that was true then and is
NOT true of every Phase 4 line below, so the claim has been narrowed rather than repeated.

Everything from Phases 1-3, plus all of Phase 4:

**Identity.** Real favicons everywhere — two masters per site (the small purpose-drawn one
for tabs, the apple-touch-icon for Start Page tiles), cached to disk, surviving a hard kill.
The monogram survives as the fallback and its eight hues now live in the theme dictionaries,
so High Contrast turns them into bordered ButtonFace boxes instead of squares that vanish.
Aviary's own pages show the dome mark in the tab, not a letter.

**The sidebar.** One surface, four panes (Tabs / Bookmarks / Reading List / History) with a
segmented switcher, a drag grip that resizes 180-420px, and a 220ms width animation. Setting
the tab layout to Sidebar collapses the horizontal strip and moves the tabs into the Tabs
pane, which is how Safari does vertical tabs. Hiding the sidebar while it holds the tabs
flips the layout back to Top bar — Aviary must never reach a state where the tabs are gone.

**The Start Page.** Favourites, Frequently Visited (never repeating a favourite), a Reading
List row, a Privacy Report card with a real seven-day chart, and a Customise popover. The
background is a parametric glasshouse in inline SVG — about 4 KB, not a photograph.

**Reader.** A from-scratch Readability-style extraction, nine fonts, four backgrounds, and a
per-site "always use Reader here". The Aa menu offers it only when the page genuinely has an
article, and says so when it does not.

**The Smart Search Field.** Suggestions from history and favourites, a Top Hit, inline host
completion, arrow-key selection. Entirely local — network suggestions are a Phase 5 setting,
off by default.

**Privacy.** Ad and tracker blocking with a seven-day rolling report; Clear browsing data is
real (history, cookies, cache, site settings, and the engine's own store). The report stores
counts per day, per tracker and per site, and deliberately never pairs a site with a tracker.

### What has actually been exercised in the running app

**7 Sep:** launch with no crash log; sidebar opens, switches panes, renders Bookmarks; real
favicons in tabs, favourites bar and Start Page tiles, surviving a hard kill; the Start Page;
Reader on a real article; the Aa menu's enabled/disabled logic; Smart Search's Top Hit; all
38 settings keys through the schema handshake; phase badges going live on cue; history on disk.

**10 Sep (Phase 5 M0 — the settle-the-debt pass):** tab drag-to-reorder after the TabChrome
refactor (works); live Liquid Glass swap with every new component present (works); light/dark
swap (was broken — fixed, see bug 25); pinning (was broken — bug 26) and pin-restore after a
cold relaunch; the tab and page context menus; the resize grip (bug 27); the reading list add
and pane; the Privacy Report on a tracker-heavy site (bugs 28 and 29); Clear browsing data,
confirmed on disk and in the engine's cookie store.

**Still never exercised by hand:** vertical tab reordering in the sidebar, and removing a
reading-list item. Treat both as unproven.

**About Clear browsing data and cookies:** after clearing, the engine's cookie store kept two
`.theverge.com` rows — because The Verge was still open in another tab and re-set them within
seconds. The SQLite freelist showed the rest were genuinely deleted. That is how Safari and
Chrome behave too; clearing does not close tabs. Do not "fix" it by reloading tabs after a
clear, which re-creates the cookies immediately. The Phase 5 experiment "Forget everything
when Aviary closes" is the real answer for wanting it all gone.

Also: history with day grouping, reading list with unread state, pinned tabs restored at
launch, our own page and tab context menus (Chromium's was Edge's, complete with Bing),
page zoom (Ctrl +/-/0) applied as CSS zoom, reload becoming a stop button mid-load, and
Ctrl+1..9 / Ctrl+Shift+Tab / Ctrl+H / Ctrl+J / Ctrl+Shift+D / Ctrl+Shift+L / Ctrl+Shift+R.

## 8. Known gaps and open items

- **Download progress ring** — deferred from Phase 4 to Phase 5. Downloads still work; the
  button is the plain one with a MenuFlyout, and downloads are still session-only with no
  history. A complete spec exists (see section 12).
- **Tab groups** — deferred to Phase 5. Pinned tabs DID ship.
- **Liquid Glass is still approximated.** WinUI has no per-element backdrop blur. Real glass
  needs `Compositor.CreateBackdropBrush` + `GaussianBlurEffect` via **Win2D** — a NuGet
  package, so ask Grant first. Today it is flat translucent fills over the window's Mica.
- **Toolbar tinting** (sampling the page colour, flipping glyphs on YIQ brightness) is in the
  Phase 0 study and still not implemented.
- **No GitHub repo.** `build.yml` has never run.
- Privacy toggles still cosmetic: DNT and third-party cookies. Everything else in that
  section is now real.
- Bookmarks have no folders or reordering.
- `Aviary.csproj` floats `Microsoft.WindowsAppSDK` at `1.7.*` — pin once CI is green.
- No app icon.

## 9. Design details that matter (Grant judges on appearance)

- **Reload lives INSIDE the address field**, right edge; **"Aa" inside** at the left. Reload
  outside the field is the loudest Chrome tell there is.
- **The address field is a 7px rounded rect, never a pill.** Pills are Chrome/Edge.
- **Idle address shows the registrable domain only**, centred. Focused shows the full URL,
  left-aligned, selected — and that switch is **instant**, never animated.
- **Tabs divide the full strip width** with no maximum. Capping them gives Chrome's
  silhouette.
- **Tab close replaces the favicon on hover**, cross-fading in the same 16px cell — Safari's
  most-copied micro-interaction.
- **Caption buttons are 46px wide and fill the toolbar row's height** (`PreferredHeightOption
  .Tall` → 46×48). Full-bleed rectangles, no radius, no gap, no fade. Close hovers
  **#C42B1C**, not the Windows 10 red.
- The window icon and its system menu are hidden — Safari has neither, and it stops Windows
  swallowing clicks where Back sits.
- Toolbar glyphs are hand-drawn `Path` at 17px optical, 1.6px stroke, round caps. **Not**
  Segoe Fluent Icons — those read as Fluent, and "generic Fluent defaults are a failure
  state".
- Safari's toolbar is 52px; **48px** was chosen so caption buttons land on Windows' exact
  Tall size.

---

## 10. Bugs already found and fixed — do not reintroduce

1. `WindowId` unqualified → CS0246. It lives in `Microsoft.UI`, which importing
   `Microsoft.UI.Windowing` does not bring in.
2. `TypedEventHandler` needs `using Windows.Foundation;`.
3. `AddScriptToExecuteOnDocumentCreatedAsync` — note the **"On"**.
4. `.sel__pop { display:grid }` **defeated the `hidden` attribute**, so every settings
   dropdown was open on load and none could close. Fixed with `.sel__pop[hidden]{display:none}`
   placed *after* the display rule.
5. `NavigateToString` has no `<title>`, so the whole page base64'd into a data: URI landed
   in the window title. Internal pages now carry a title, and `BrowserTab._internalTitle`
   stops `DocumentTitleChanged` relabelling our own tabs.
6. `AppWindow.Resize` takes **physical** pixels — scale by `GetDpiForWindow`.
7. `AppWindowTitleBar.RightInset` is in device pixels — divide by `RasterizationScale`.
   Never let the caption spacer collapse below ~100px.
8. https was forced onto anything containing a dot, making **localhost unreachable**.
   `LooksLocal()` now excepts localhost / *.localhost / 127.* / 0.0.0.0 / ::1.
9. Failed navigations showed **Chromium's error page with a Microsoft Edge logo**. Aviary
   draws its own now, and hides the WebView behind it.
10. A navigation that becomes a download reports itself **aborted**, so every successful
    download was followed by a false error. Guarded by `_navigationBecameDownload`.
11. Pinning the app to Light left pages dark — fixed with
    `CoreWebView2Profile.PreferredColorScheme`.
12. A flex child needs `min-width: 0` or it refuses to shrink and pushes content off-window.
13. `BuildSettingsHtml` injected a hand-written list of 13 keys while the handler saved all
    22, so NINE controls wrote to disk and rendered their default next visit. Fixed by the
    schema handshake: the page posts its own defaults up on load, so SAVING a key is what
    registers it and there is no second list. Never reintroduce an allow-list here.
14. The "Reset settings" button posted itself as a setting, writing a junk `reset:true` key
    and doing nothing. Buttons now carry `data-cmd` and dispatch as commands.
15. Version and phase were hardcoded in four places and went stale the moment a phase
    shipped. `BuildInfo.cs` is the only source now, and phase BADGES are derived by
    comparing a row's number against `PhaseShipped` — a badge cannot outlive its phase.
16. A Windows path substituted into a JavaScript string literal loses its separators:
    `D:\Files\Downloads` rendered as `D:FilesDownloads`, because `\F` and `\D` are escape
    sequences. HTML-encoding does not help. Paths are JSON-encoded and set as textContent.
17. `OnNewWindowRequested` called `Navigate` on the CURRENT tab, so `target="_blank"` replaced
    the page you were reading. Now takes a deferral and hands the engine a real second tab.
18. `Deferral` is `Windows.Foundation.Deferral` in this project, NOT `CoreWebView2Deferral` —
    that name is the .NET wrapper's, and this binds the WinRT projection.
19. `Border` is SEALED in WinUI 3. `SidebarGrip` derives from `Grid`.
20. `CoreWebView2Controller.ZoomFactor` is unreachable: WinUI 3's WebView2 owns its
    controller privately. Zoom is CSS `zoom` on the document element, re-applied after every
    navigation because the document it lived on is gone.
21. A stock `ToggleButton`'s Checked state paints the SYSTEM ACCENT from the Fluent theme and
    overrides any Background set in code. The sidebar segments needed a real ControlTemplate.
22. A `Path` that is a control's CONTENT is outside the template's namescope, so a
    `RelativeSource TemplatedParent` binding resolves to nothing and the icon renders
    invisible with no error. `Path.Stroke` is not inherited either. Assign it and re-stamp
    on theme change.
23. Favicon files were written immediately but `index.json` only at shutdown, so a crash or
    force-kill orphaned the whole cache. The index is now written on a 2-second debounce.
24. The injected `ShortcutScript` rejected Alt outright and ignored Shift, so `Ctrl+Shift+D`
    lowercased to `d` and fired plain bookmark. It now builds a modifier-qualified combo, and
    reads digits from `e.code` so a layout where the number row is punctuation still works.
25. **Code-side brushes resolved against the WINDOWS theme, not the pinned one.**
    `Application.Current.Resources[key]` picks a theme dictionary by the application's theme,
    and Aviary pins its theme on `RootGrid`. Pinned Light on a dark machine: a dark Start Page
    inside light chrome, white sidebar glyphs and headers on light grey, white caption buttons
    on a light toolbar, dark menus. Invisible whenever the two themes match, so it never
    showed until the theme was switched in the running app. Fixed by `MainWindow.Theme.cs`
    (`ThemeBrush`, `ApplyCaptionColors`, `Themed`, `OnEffectiveThemeChanged`). **Never read a
    brush from `Application.Current.Resources` in code; use `ThemeBrush`.**
26. **Pinning threw on every attempt.** `TogglePinned` removed the tab from `_tabs`, then
    called `PinnedCount()` — which counts pinned tabs *in* `_tabs` — so the count was 0 and
    `Insert(-1)` threw. The throw landed after the removal, so the crash handler kept the app
    alive with the tab on screen but gone from the list, and the + button then appeared dead.
27. **The resize grip only grabbed a third of its gap under Liquid Glass**: 6px of grip beside
    the content sheet's 12px inset. `ApplyContentInsets` gives the grip the whole gap while
    the sidebar shows. It must NOT reach over the web page under Classic.
28. **Privacy Report bars used % heights against a content-sized parent**, so every bar was
    its 3px minimum whatever the data — invisible for as long as the count was zero.
29. **The Privacy Report only saved every 100th block and never on exit**, so the "seven-day"
    report reset on every launch. Saves are now debounced per block, and `MainWindow_Closed`
    flushes both it and `History`.
30. **`MenuFlyoutPresenterStyle` with a `RequestedTheme` setter does nothing** for flyout
    theming in this WinUI build. It compiles and the menu still renders in the Windows theme.
    Set `RequestedTheme` on the live popup element in `Opened` instead (`ThemeOpenPopups`).

---

## 12. The recovered Phase 4 design specs

A ten-agent design panel ran on 6 Sep 2026 and died halfway on the account's session limit.
FIVE specs completed and were recovered rather than re-run (the fan-out cost 1.6M tokens):
Start Page, sidebar, favicons, Smart Search Field, and downloads + the settings pass. They
are ~439 KB of implementation detail — exact APIs, all six theme-token values per key, and
long gotcha lists that caught several real traps before they were hit.

**The downloads spec is the one still unspent** — it covers the progress-ring button, the
popover, persisted download history and Safari's retention policy, and Phase 5 should start
from it rather than redesigning. If the scratchpad is gone, the run is recoverable from
`subagents/workflows/wf_34c9534a-69c/` — but note `journal.jsonl` logs `value:null` for
agents that DID finish; the real payload is in the task output file.

Lesson worth keeping: when a fan-out dies on the session limit, recover the completed
agents' work before considering a re-run.

## 11. Suggested first message for the new chat

> Continue the Aviary browser project in `Zoo/`. Read `Zoo/HANDOFF.md` first — it has the
> full state. Phase 4 is done; start Phase 5 (Alpha Build 5). The download ring and tab
> groups carried over from Phase 4 — section 12 says where the unspent spec for the
> download ring is.
