# Aviary — project handoff

Everything a new session needs to pick this up. Written 6 September 2026, at the end of
Phase 3.

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
| **04** | alpha | **Safari features** | **NEXT** |
| 05 | alpha | Alpha Build 5 (pre-release) | planned |
| 06 | beta | Beta Build 1 | planned |

**Phase 04** — the real Safari Start Page, Reader mode, bookmarks + reading list sidebar,
history, and the second 25% of features. Also where the **vertical sidebar tab layout**
(requirement 4) should land.

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

## 7. What works today (all verified by running it)

Tabs (new/close/switch/**drag to reorder**) · Ctrl+T/W/L/Tab/F/D · address bar with
domain-only idle display and Safari's centre→left focus behaviour · back/forward/reload/home ·
app menu · **Settings with 13 sections** · **both looks switching live and remembered** ·
Mica visible under Liquid Glass · light/dark following system or pinned (pages follow too,
via `PreferredColorScheme`) · **real ad/tracker blocking with a live counter** (24 requests
blocked on The Verge) · memory saver on background tabs · **find in page** (92 matches on
Wikipedia) · **downloads** with a toolbar button · **favourites bar** with Ctrl+D · settings
and bookmarks persisted atomically · Safari user agent (the Start Page reports it) ·
Aviary's own error pages.

**Four settings genuinely drive the app:** theme, search engine, home button, show-full-URL —
plus look, block-trackers, memory-saver, favourites-bar. The rest are honestly labelled with
the phase they arrive in.

---

## 8. Known gaps and open items

- **Vertical sidebar tab layout is NOT built** (requirement 4). Only the top bar exists.
  The sidebar toggle button in the toolbar is inert.
- **Liquid Glass is approximated.** WinUI has no per-element backdrop blur. Real glass needs
  `Compositor.CreateBackdropBrush` + `GaussianBlurEffect`/`SaturationEffect` via **Win2D**
  (`Microsoft.Graphics.Win2D`) — **a NuGet package, so ask Grant first.** Today it is flat
  translucent fills over the window's Mica. The honest limits are documented at the top of
  `LiquidGlass.xaml`.
- **Toolbar tinting** (sampling the page colour and flipping glyphs on YIQ brightness) is
  specified in the Phase 0 study but **not implemented**.
- **No GitHub repo.** `build.yml` has never run. Creating the repo and the first push need
  Grant's account.
- Privacy toggles other than tracker-blocking (DNT, third-party cookies, clear data) are
  still cosmetic — due by Phase 5.
- Downloads are session-only; no history. Bookmarks have no folders or reordering.
- `Aviary.csproj` floats `Microsoft.WindowsAppSDK` at `1.7.*` — pin once CI is green.
- No app icon (`<ApplicationIcon>` intentionally absent until the .ico exists).

---

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

---

## 11. Suggested first message for the new chat

> Continue the Aviary browser project in `Zoo/`. Read `Zoo/HANDOFF.md` first — it has the
> full state. Phase 3 is done; start Phase 4 (Safari features).
