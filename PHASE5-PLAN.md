# Aviary — Phase 5 "Alpha Build 5" plan

## Context

Phase 5 is the **last alpha** — the build good enough that Phase 6 (Beta Build 1,
"Testing 1") can be lived in. Grant asked for: the original Phase 5 plan, the two items
carried from Phase 4, new features, optimisation and polish ("cleaner, smoother, more
dynamic"), and — his headline — **privacy that actually works**, inspired by Brave: a real
ad blocker, Brave Search, and an opt-in **local diagnostics folder** I read every turn.
After reviewing the first draft he added three requirements: **diagnostics must genuinely
work**; **optimisation and polish must be good enough for diagnostics to measure them**;
and **the Experimental menu must hold a lot, explain everything in plain English for people
who don't know tech, and never let anyone make themselves less safe.**

An audit of the 7 Sep build found why "privacy that works" is not a slogan:

- **Three Privacy toggles are live, ungated, and do nothing.** `dnt`, `safariUa`,
  `thirdPartyCookies` exist only in `AppSettings.cs` Defaults; nothing reads them. The
  Safari UA is set unconditionally at `BrowserTab.cs:122`.
- **15 of 33 settings do nothing.** Never read: `askDownload`, `autoplay`, `background`,
  `gpu`, `language`, `minFont`, `savePasswords`, `scrollbars`, `suggest`, `translate`.
  Defaults only: the three above plus `reduceMotion`, `startup`.
- **71 exceptions are silently swallowed** (`catch (Exception) {}`: 16 in
  `MainWindow.xaml.cs`, 12 in `Favicons.cs`, 9 in `BrowserTab.cs`). Each is a failure no
  one sees — the case for building diagnostics *first*.
- `WebViewEnvironment.cs:61` passes an **empty** `CoreWebView2EnvironmentOptions()` — the
  unused hook for GPU, autoplay, language, frame-rate and dark-mode flags.

## Decisions made with Grant

- Direction is **Brave-inspired**: real blocking, GPC instead of the dead DNT header,
  tracking-parameter stripping, Brave Search.
- **Diagnostics gathers everything and lists everything it gathers.** Grant, knowingly:
  "I know it's against our vision but we need the data… all data is required for our
  improvement." So: every category of activity, full URLs and titles, stored **locally
  only**, no network code in the feature at all. One switch, all-or-nothing — no partial
  modes. Kept opt-in (off until switched on), per his original "once enabled" wording.
- **The one exclusion is secrets, and it is a safety line, not a privacy one:** passwords,
  anything typed into website forms, cookie values, login tokens, card numbers, clipboard
  contents. None of it helps fix a bug, and a plaintext file of it — read by me every turn
  — would be a credential leak, which the "stay safe" requirement rules out.
- **Ad blocker: "selective, you pick"** → a real filter-list engine plus per-site control.
- Data folder named exactly `user.data.improvementcollection`.
- Experimental menu: **many flags, plain-English, safe by construction.**

## Build order

Each milestone ends in a **published build verified by running it**. Diagnostics comes
first so every later milestone is recorded — and every optimisation is measured by it.

### M0 — Settle Phase 4's debt · **done 10 Sep 2026**
Exercise everything HANDOFF §7 marks as never run, and fix what fails: Liquid Glass swap
with the new sidebar/favicons/Start Page present; light/dark swap; **tab drag-to-reorder
after the TabChrome refactor** (all four handlers rewritten; has died silently twice
before); vertical reorder; pin + pin-restore; both context menus; resize grip; Privacy
Report on a tracker-heavy site; reading list add/remove; Clear browsing data. Commit
Phase 4, split by feature.

### M1 — Diagnostics that gathers everything: `user.data.improvementcollection` · **done 11 Sep 2026** (HANDOFF §13)
New `Diagnostics.cs`, folder `%LOCALAPPDATA%\Aviary\user.data.improvementcollection\`,
created the moment the setting is switched on. **Everything below is gathered, all at
once — one switch, no partial modes.**

**What is gathered — the complete list, 16 categories:**

| # | Category | Detail | File |
|---|---|---|---|
| 1 | Browsing | every navigation: full URL, title, referrer, redirect chain, HTTP status, timing, result | `browsing.jsonl` |
| 2 | Searches | every query typed in the address bar, the engine used, which suggestion was chosen | `browsing.jsonl` |
| 3 | Network | every request the browser makes: URL, type, status, size, timing, first/third party | `network.jsonl` |
| 4 | Blocking | every blocked request: full URL, the rule that matched, the page it was on; every Shields change | `blocking.jsonl` |
| 5 | Interactions | every chrome action: buttons, menu items, shortcuts, tab open/close/switch/reorder/pin, sidebar panes, look and theme swaps, find-in-page terms, zoom | `events.jsonl` |
| 6 | Settings | every change, old → new value, plus a full snapshot each session | `events.jsonl` |
| 7 | Experiments | every flag toggled, and which were on when anything went wrong | `events.jsonl` |
| 8 | Website errors | JavaScript errors and failed resources on pages, captured per page — why a *site* broke | `pages.jsonl` |
| 9 | Engine failures | WebView2 `ProcessFailed` events: renderer, GPU and browser process crashes, hangs | `issues.jsonl` |
| 10 | Swallowed errors | **all 71 `catch (Exception) {}` sites, finally visible**, with location and stack | `issues.jsonl` |
| 11 | Anomalies | self-detected: settings that didn't round-trip, favicon failures, WebViews that didn't start, UI stalls over 50 ms | `issues.jsonl` |
| 12 | Crashes | exception, stack, build, last ~100 actions, every open tab's URL | `crashes/<time>.json` |
| 13 | Performance | startup phases, tab-create time, navigation timing, filter match cost, memory and CPU per tab, frame timing | `perf.jsonl` |
| 14 | Features | how often each feature is used — Reader, sidebar, pins, Start Page sections — and Reader's readable/not-readable verdict per page | `events.jsonl` |
| 15 | Downloads | file name, source URL, size, result | `events.jsonl` |
| 16 | Environment | Windows build, WebView2 version, CPU, RAM, GPU, every display's scale and refresh rate, window size, tab count | `session.json` |

**Never gathered — secrets:** passwords and anything typed into password fields; what you
type into website forms; cookie values, `Authorization` headers and session tokens; card
numbers; clipboard contents. Logged URLs have secret-bearing parameters **masked, not
dropped** — `access_token`, `token`, `code`, `auth`, `session`, `password`, `key`,
`secret`, `sig` keep their name with the value replaced by `•••` — so the URL is still
useful for debugging but a password-reset or login link is not a working credential on disk.

**"Lists everything it gathers":**
- `manifest.json` in the folder names every category above, its file, and what it
  contains — the folder explains itself.
- The Settings row has a **"What Aviary collects"** disclosure listing all 16 categories
  and the secrets exclusion, in plain English.
- `summary.md` opens with the same list, then the digest.

**Mechanics:**
- Each `catch (Exception) {}` becomes `catch (Exception ex) { Diagnostics.Swallowed(ex, "where"); }` —
  a near-free no-op while collection is off.
- Background writer on a channel; the UI thread only enqueues, never waits on disk. Network
  logging is batched so a busy page cannot flood the writer.
- **200 MB** rolling cap (network logging needs room) — **raised to 500 MB in M2.5** —
  **30-day** retention, rotated per file, both shown in the row. **Private windows are never
  recorded.**
- Row at the **bottom of Privacy**, stating plainly that it records every web address,
  page title and search. Buttons: **Open folder**, **Delete collected data**. Clear
  browsing data offers to clear it too.
- `summary.md` is regenerated on exit and every 10 minutes: complete category list, crash
  count, top recurring issues, slowest operations, sites with the most page errors,
  engine failures — small enough to read each turn when the raw logs are not.
- **"Genuinely works" is a test, not a claim** — see Verification: switch on, browse,
  force an error, force a crash, read every file back, switch off, confirm silence.
- **Standing practice:** once the folder exists I read `summary.md`, `session.json`, any
  crash newer than last turn, and the tail of `issues.jsonl` at the start of every turn,
  dipping into the raw logs only to chase a specific issue — with `cat`/Read, **never
  Python** (it sees a virtualized stale copy of LocalAppData here; HANDOFF §5). What I
  report back is bugs and their causes, not a recital of where Grant browsed. Saved to
  memory when execution starts.

### M2 — Privacy that actually works · **done 13 Sep 2026** (HANDOFF §14)
Built and tested with each switch on and off: third-party cookies (engine switch), GPC
(engine switch), WebRTC policy (engine switch — not measurable without camera permission),
tracking parameters (navigation AND request level), HTTPS-Only with a fallback panel, referrer
trimming (header AND document.referrer), the Safari identity with client hints removed, a
private window, saved passwords, form-entry autofill as its own switch. Added at Grant's
request (12 Sep): **F11 full screen**, and a **Privacy Report with a 7-day / all-time choice**
(in Settings and on the card) showing Brave's three figures — blocked, bandwidth saved, time
saved (estimates, labelled). Decisions: engine switches apply at the next launch and say so;
no Restart button until session restore exists. Finished 13 Sep with **Website data** (per-site
cookies and storage, Remove per site) and, from Grant's own diagnostics, **one Aviary per
profile** (a second launch hands itself to the running one — HANDOFF bug 51). Testing Remove
found that the engine clears EVERY site for an origin it cannot parse (bug 52, fixed before it
shipped). Closed, at Grant's ask, with an **Updates** section in Settings and the build names:
phases 1–5 are Alpha Build 1–5, phases 6–7 Beta Build 1–2, and this build calls itself **Alpha
Build 5 (AB5)**, a pre-release (HANDOFF §15). Committed 13 Sep (`6f44c59` + the Updates commit).
- **Fix the three lying toggles.** `safariUa` genuinely switches the per-tab UA.
  `thirdPartyCookies` blocks cookies on third-party requests in `OnWebResourceRequested`.
  **`dnt` becomes Global Privacy Control** (`Sec-GPC: 1` + `navigator.globalPrivacyControl`)
  — DNT is dead and an uncommon DNT header is itself a fingerprinting bit.
- **Tracking-parameter stripping** (Brave's Query String Filter): remove `utm_*`, `fbclid`,
  `gclid`, `mc_eid`, `igshid` and similar from navigations and copied links. On by default.
- **HTTPS-only** with a clear fallback page and per-site exception (via `SiteSettings`).
- **Referrer trimming** to origin on cross-site requests.
- **Fingerprinting reduction**: trim client hints; honest labelling where WebView2 can't reach.
- **Private window**: separate in-memory profile; `History.Record` already checks `privateMode`.
- **Save passwords** made real via WebView2's password-autosave setting.
- **Website data**: per-site list of cookies/storage with remove.

### M2.5 — The Collection · **done 13 Sep 2026** (HANDOFF §16)
Grant's "middle update", added between M2 and M3. His projects inside Aviary, running offline at
`https://collection.aviary/`: the whole Wally series (the Complete Edition plus Silver, Radio, the
v2.5 folder's cuts and the rest found across Documents) and everything else in the CoWork folder
bar his six exclusions — 49 exhibits, copied, never edited. Wally2TTD 2.4.2 exported for the web
and Jurassic Empire's own web build both play in a tab. New landing pages for what a browser
cannot run — **Wally2TTD 2.4.1 & 2.4.2** (one page for both, as asked), the Wally2TTD Launcher,
WallyCore1, TinyCalc. Entry: app menu, a Start Page row, the address. Also: a description under
every milestone in Updates (and M2.5 itself), and the diagnostics cap raised to **500 MB**.

### M3 — The ad blocker: filter engine + selective control · **done 13 Sep 2026** (HANDOFF §17)
Built as planned, with these outcomes and differences — each decided on evidence:
- **Lists:** Grant approved EasyList + EasyPrivacy only (13 Sep). The optional cookie-banner and
  social lists were NOT added; each needs his yes. So the "per-category switches" are **Ads**
  (EasyList) and **Trackers** (EasyPrivacy).
- **Standard / Strict follow Brave:** Standard stops what other sites load into a page and never a
  site's own requests (any list, Aviary's included); Strict applies everything and warns before
  opening a site the lists mark as ad/tracker-only, with **Open Anyway**.
- **Index:** plain `||host^` rules (89,000) in a host table; the rest filed by their RAREST
  whole-word token rather than the longest. Regex rules, `$popup`, `$csp`, `$redirect`, scriptlets
  and procedural hiding are skipped and counted — shown in Settings.
- **No compiled index on disk:** building from text takes 70–110 ms on a background thread at
  launch, inside the first page's wait; a cache is left for M7 to justify with numbers.
- **Cosmetic:** one page script in every document, nested frames included, adopting one
  stylesheet; measured first (~2 ms per page, no recalc cost). Blocked frames and images also
  collapse, and blocked frames get a 204 so they no longer cost a renderer process.
- **Match cost:** ~1.6 µs a request in the harness, ~9 µs in the app (budget: well under 1 ms).
- **The weekly refresh is off by default**; "Update now" is the user-initiated path.

The original M3 plan:
New `FilterEngine.cs` (parser + token index) and `Shields.cs` (per-site state).
`TrackerBlocker` stays as the fast pre-filter.
- Network rules that carry most of the benefit: `||domain^`, `|` anchors, substrings,
  `$third-party`, `$script`/`image`/`stylesheet`/`xmlhttprequest`/`subdocument`,
  `$domain=`, `@@` exceptions. Indexed by each rule's longest literal token.
- **Cosmetic filtering**: `##` rules injected as one stylesheet per document so blocked ads
  leave no empty boxes.
- **Lists**: EasyList + EasyPrivacy bundled as **unmodified data files with attribution**
  (GPLv3 / CC BY-SA 3.0 — never merged into our own list, which would trigger
  share-alike). Optional categories: cookie banners/annoyances, social widgets. Update is
  user-initiated plus an optional weekly refresh.
- **Selective**: Standard / Strict; per-category switches; a **per-site Shields toggle** in
  the toolbar shield popover, which also shows this page's blocked count and opens the full
  Privacy Report.
- Parsed once on a background thread, compiled index cached to disk. **Match cost is
  logged to `perf.jsonl`** with a budget (see M7).

### M4 — Search · **done 14 Sep 2026** (HANDOFF §18)
Grant (14 Sep): DuckDuckGo stays the default; Bing and Google stay on the list. Built as planned:
one engine table (`SearchEngines.cs`) behind the Settings list, the address bar and the context
menu; engine suggestions real — off by default, "per-engine" made concrete as **changing engine
switches them off**, never from a private window, never an address, no cookies or redirects.
Mojeek has no suggestion service (its switch greys out). Found on the way: a private window kept
searching the old engine (fixed), and bug 62 (a website could hear settings changes — fixed).
Found from Grant's connection: **Qwant does not serve the Philippines** ("not yet available in your
country") — so, at Grant's word, it was taken off the list: Brave Search, Mojeek and Ecosia joined.

The original M4 plan:
- Add **Brave Search** (`https://search.brave.com/search?q=`) plus Mojeek, Qwant, Ecosia
  beside DuckDuckGo (default) and Startpage.
- Network suggestions (`suggest`): off by default, per-engine, and the row says it sends
  each keystroke to that engine.

### M4.5 — Smooth Flight · **done 20 Sep 2026** (HANDOFF §21)

Grant's second middle update, between M4 and M5: performance, bugs, motion and a great deal
more. He named it, and the name carries twice over — **Smooth Flight** is the milestone and
Classic's way of moving; **Fluid** is Liquid Glass's.

- **Motion** (`Motion.cs`, `MainWindow.TabMotion.cs`, `MainWindow.ChromeMotion.cs`): each look
  moves its own way, all of it on the compositor thread; Appearance → Animation style; Reduce
  motion turns every animation off and now tells websites too.
- **Bugs**: 67 (the idle freezes were Windows' move/size loop — stalls are now measured as busy,
  waiting, machine paused or window held), 68 (tabs listed in the sidebar in Top bar layout),
  69 (a tab with no name for a screen reader), 70 (`TabFor` never matching, so Settings kept
  showing stale values).
- **Speed, measured**: the blocker's worst check 3.5–7.4 ms → under 1 ms (run-time JIT, not the
  rules); the tab strip reuses its buttons; leaving full screen is 85–120 ms inside the SDK's own
  `SetPresenter` and is now timed permanently rather than guessed at.
- **Settings → Performance** (`LiveStats.cs`, `GpuCounters.cs`) and **Settings → Storage Usage**
  (`StorageUsage.cs`), both checked against Windows' own figures.
- **Shields**: two more lists (Cookie notices, Social widgets), per-site Off/Standard/Strict,
  My filters, what Shields stopped, per-list detail and "Use the shipped lists".
- **Accessibility**: text size for Aviary's own pages, underlined links, a clear keyboard focus
  ring, every shortcut listed; and two tab choices (ask before closing several, what the last
  tab does). Diagnostics cap 500 → 650 MB.

**What this takes off M7's list**: the blocker's per-request cost, the tab strip rebuild, the
focus rings, the animated pane transitions, the chrome dimming when the window is inactive, and
one easing set used everywhere. M7 keeps the broader budget work — cold start, new-tab time,
memory per background tab, and the long-session numbers.

### M4.6 — Smooth Fix · **done 20 Sep 2026** (HANDOFF §22)

Grant's third middle update, and the one that renamed the build: **Alpha Build 5 is "The Long
Run"** (`BuildInfo.PhaseName`), because it has now run M0 → M4.6.

- **Full screen moves** (`MainWindow.FullScreen.cs` + `Motion.LiftAway/SettleBack`): the chrome
  lifts away and the presenter changes on the animation's last frame; coming back, the presenter
  changes first and the chrome settles after. `presenterMs` now lands INSIDE `animateMs`.
- **Motion**: Animation speed (Calm/Standard/Quick, one multiplier reaching the pages through
  `--m-x`), menus and popovers bloom from their opener, and a pointer-following gleam on the glass.
- **Liquid Glass → Window buttons** (Floating / Buttons on the glass / Bar aligned) — the bar moves
  to Windows' buttons, since the buttons themselves cannot move (M4.5's finding).
- **Storage Usage**: per-site stores where the engine's folders name an origin, the shared
  databases reported as shared, the graphics caches cleared at the next launch (before the engine
  opens them), the 650 MB cap drawn, and a re-measure every 10 s while the pane is open.
- **Performance**: per-tab data (WebResourceResponseReceived attached only while watching), GPU by
  engine, commit and peak memory, a per-tab sort, and a 1/2/5-second sampling choice.
- **Tabs**: new-tab position, new tab opens with the current page, middle-click closes, and the
  close cross always/on hover/never.
- **The app icon**, at last: `tools/icon/build-icon.ps1` draws the birdcage dome on a blue tile and
  on a glass tile, ten sizes each; `<ApplicationIcon>` carries the blue one and Accessibility →
  App Icon switches the running window between them.
- Bugs 73 (the settings round-trip Grant's own log caught) and 74 (Composition's "Translation"
  cannot be stopped, which silently killed the first F11 animation).

### M4.7 — Immediate Fix · **done 21 Sep 2026** (HANDOFF §23)

Not planned: M4.6 shipped that same night with **every menu in Aviary dead**, and Grant asked for
"a immediate bug fix and patch now" the next morning. The milestone exists because the bug did.

- **Bug 75** — the arrival M4.6 gave menus was written onto `popup.Child`, the popup's own root
  child, whose visual is how the popup site places AND hit-tests the content. Menus drew where they
  belonged and answered the mouse ~860 px away, so every click on one read as a click outside it:
  the menu dismissed and chose nothing. `ThemeOpenPopups` is the walk every Aviary menu passes
  through, so the app menu (Settings, The Collection, Downloads), the Aa menu, the tab menus and the
  sidebar's all went at once. `BloomInto` now blooms the presenter's template root, one level in.
- **Bug 76** — `PutRowBack` animated `Translation` on rows that never had it enabled (the ones
  hidden when the chrome lifted away), which threw and skipped the `Opacity = 1` after it. Grant
  runs with the bookmarks bar off, so it fired on his F11.
- **Three tools added to the kit**, because four earlier attempts could not tell the broken build
  from the good one: `flyout-geometry.ps1`, `menu-click.ps1` (and `bloom-frames.ps1`, which was later
  found not to measure the bloom at all — HANDOFF §25).
- **The rule this cost:** a composition transform through `ElementCompositionPreview` is render-only
  on an ordinary element and is NOT on a popup's root child. And always run the control.

### M4.8 — A Iconic Fix · **done 21 Sep 2026** (HANDOFF §24)

Also unplanned, and named by Grant: four asks while he was testing M4.7.

- **The app icon (bug 77)** — it worked and was invisible. Aviary draws its own title bar, so it has
  no icon inside itself, and with no `AppUserModelID` the taskbar drew the .exe's icon instead of
  the window's. `Program.TellTheShellWhoWeAre` gives Aviary a shell identity before the first
  window; the row draws both tiles and rings the one in force. **Grant owes the taskbar check** —
  Windows 11's taskbar cannot be read from a tool here.
- **Performance, like Task Manager** — every part of Aviary a line with its own columns (Process,
  Memory, Processor, Graphics, GPU mem, Handles), sortable by any of them, each page process's tabs
  nested under it, a total at the foot, and cells shaded against the busiest row rather than fixed
  thresholds (fixed ones left every cell blank on an idle machine).
- **Memory saver that saves memory (bug 78)** — `sleepIdleTabs` suspends a tab nobody has looked at
  (Never / 5 / 15 / 30 min / an hour; 30 by default). Never the active tab, a private tab or one of
  Aviary's own pages, and the engine refuses for anything playing sound or video. 40 MB → 1 MB
  measured. Bug 78 on the way: nothing had ever called `Resume()`.
- **Storage Usage** — every category row draws its share as a bar in its own colour.
- Kit: `window-icon.ps1`, `sleep-test.ps1`, and `AVIARY_SLEEP_TEST=<seconds>` (environment only).

### M4.9 — Birds News · **ALL THREE STAGES DONE, 21–24 Sep 2026** (HANDOFF §25)

Grant's ask, while testing M4.8. Plan: `~/.claude/plans/fix-one-more-thing-tingly-elephant.md`.

- **Stage 1 — done.** Memory saver as one row of four modes (Off / Chill / Balanced / Aggressive,
  which also lets go on minimise); M4.8's `sleepIdleTabs` folded in before it ever shipped;
  diagnostics cap 700 MB; My filters text collected (bug 72 reversed at his word); frame timing as
  an opt-in that is off even while collection is on; bugs 79 and 80.
- **Stage 2 — Birds News — done.** Six approved feeds (BBC, Al Jazeera, The Guardian, Inquirer, Rappler,
  GMA) on the Start Page under Favourites and on a Birds News page of its own (the Start Page opened on
  the news alone, rather than a separate `birds.aviary`), pushed live, fetched as Aviary itself, never
  from a private window, headlines never rewritten. The first real fetch found Inquirer mislabelling
  its time zone (corrected from its own build date) and one publisher filling a column (now dealt
  round). Bug 81.
- **Stage 3 — his five picks.** Search open tabs (Ctrl+Shift+A), Forgetful browsing with a time
  (on close / 5 min / 1 h / 1 day), Safety Check, Start Page backgrounds, Picture-in-picture (verified
  against the engine first, badged rather than shipped dead if it cannot).

### M5 — Carried from Phase 4
- **Download ring, popover, persisted history** — straight from the recovered spec
  `scratchpad/specs/downloads-safari-progress-ring-button-po.md`: `DownloadRing.cs`,
  `DownloadsPopover.cs`, `FlyoutTheme.cs`; rows `downloadDir`, `askDownload`,
  `downloadHistory`, `warnExecutables`, `clearDownloads`, and the "never auto-open files"
  note. `FlyoutTheme` also fixes flyouts ignoring a pinned light/dark theme.
- **LinkScript** from the same spec: Ctrl/middle-click → background tab, Ctrl+Shift →
  foreground, Alt+click → download.
- **Tab groups**: named, persisted, switched from the sidebar Tabs pane.

### M6 — The Experimental menu: a lot, in plain English, safe by construction

A new **Experimental** section — one entry in `SECTIONS` — built so someone who knows
nothing about computers can use it without harm.

**How every flag is presented.** Each one has four parts, written in everyday words:
1. **Name** a person would say out loud ("Put quiet tabs to sleep", not "Tab suspension").
2. **What it does** — one or two sentences, no jargon.
3. **What might happen** — the honest downside, even when it's small.
4. **A badge**: 🟢 *Safe to try* · 🟡 *Some sites may look wrong* · 🔵 *Privacy trade-off*.
   (Drawn as small coloured labels in the page, not emoji.)

Example descriptions, to set the tone:
> **Screen smoothness** — How many times a second Aviary redraws what you see. Higher looks
> smoother on fast screens but uses more battery. *Not sure? Leave it on "Let my screen decide."*
>
> **Put quiet tabs to sleep** — Tabs you haven't looked at in a while go to sleep to free up
> memory, and wake the moment you click them. Tabs playing sound are never put to sleep.
>
> **Preload a page when you point at a link** — Aviary starts loading a link as soon as your
> mouse rests on it, so it opens faster. *The website finds out you pointed at it, even if
> you don't click.* 🔵

**The flags (20), in five groups:**

| Group | Flag | Badge | How it works |
|---|---|---|---|
| Speed & memory | Screen smoothness (let my screen decide / as smooth as possible) | 🟢 | env args |
| | Put quiet tabs to sleep (after 5 / 15 / 60 min) | 🟢 | `TrySuspendAsync` |
| | Open pinned tabs only when clicked | 🟢 | lazy restore |
| | Smooth scrolling on/off | 🟢 | env arg |
| | Mute tabs in the background | 🟢 | `CoreWebView2.IsMuted` |
| | Preload a page when you point at a link | 🔵 | prefetch |
| Looks | Make every website dark | 🟡 | force-dark env flag |
| | Tint the toolbar to match the page | 🟢 | page theme colour |
| | Show a loading bar in the address field | 🟢 | navigation events |
| | Calm animations on websites too | 🟢 | reduced-motion env flag |
| Tabs | Open new tabs next to the current one | 🟢 | tab insertion |
| | Show a preview when you point at a tab | 🟢 | `CapturePreviewAsync` |
| | Close tabs you haven't used in a week | 🟡 | idle tracking |
| | Double-click empty tab bar for a new tab | 🟢 | pointer handler |
| Privacy | Forget everything when Aviary closes | 🟢 | clear-on-exit |
| | Strip even more tracking from links | 🟡 | extended param list |
| | Block scripts from other websites | 🟡 | third-party script filter |
| | Stronger fingerprint protection | 🟡 | injected defences |
| Troubleshooting | Show a performance overlay (speed and memory) | 🟢 | **reads live diagnostics** |
| | Turn off video hardware decoding | 🟢 | env arg — for video glitches |

**Safety, by construction — this is what "lets you stay safe" means:**
- **Nothing here can make you less safe.** Flags that would weaken security are excluded
  outright and never offered: disabling certificate or HTTPS errors, the sandbox, site
  isolation, web security or same-origin rules, or opening a debugging port. This rule is
  written into the code as a comment and into the menu's header.
- The header says, in plain words: *"These are unfinished features. Some might make a
  website look wrong. None of them can make Aviary less safe."*
- **One button turns every experiment off.**
- 🔵 privacy trade-offs are off by default and ask once before turning on.
- **Crash protection that does not depend on diagnostics:** a tiny always-on "did the last
  run end cleanly?" marker. If Aviary crashes soon after an experiment was changed, the
  next launch starts with all experiments off and says so: *"Aviary switched off
  experimental features after a problem. [Turn them back on] [Keep them off]."* Safety
  must not require opting in to data collection.
- Flags that need a restart say so and offer **Restart Aviary**.
- **Every flag is proven before it ships.** If one turns out not to work on this WebView2
  build (the frame-rate cap is the likeliest), it is cut rather than shipped inert — the
  same "no live, inert row" rule as the rest of Settings.

**Also in M6:** every remaining inert setting made real or honestly gated — `gpu`,
`autoplay`, `language` via the environment options; `minFont`, `scrollbars` via injected
CSS; `reduceMotion` driving chrome animations. `translate`, `background`, `startup` stay
gated for Phase 6. **Target: zero live, inert rows.**

### M7 — Optimisation and polish, measured by diagnostics
Nothing is "optimised" on feel. Each change is **measured before and after from
`perf.jsonl`**, and the numbers go in the phase notes.
- **Budgets** (baseline taken in M1, targets confirmed then): cold start to first paint;
  new-tab time; filter-engine match cost per request (target well under a millisecond);
  memory per background tab with sleep on vs off; zero UI-thread stalls over 50 ms.
- Likely work: the `"*"` WebResourceRequested filter on every context (narrow it, keep the
  handler allocation-free); startup ordering (pinned tabs, favicon index, filter index all
  off the critical path); Start Page build cost; long-session memory.
- **Polish — "cleaner, smoother, more dynamic"**: what is left after M4.5 (which did the
  chrome dimming, one easing set, focus rings and the animated pane transitions): the favicon
  as a loading indicator, a tab audio indicator and per-tab mute, and empty states. Real Liquid
  Glass blur still needs Win2D — **ask first.**

### M8 — Beta preparation
- **GitHub repo + CI** (hard requirement 6, still unmet): prepare everything; creating the
  repo and first push need Grant's account. Pin `Microsoft.WindowsAppSDK` once CI is green.
- App icon. `BuildInfo` → `PhaseShipped = 5`, `MilestonesDone = 10` (0.5.0 and the name
  "Alpha Build 5" already arrived on 13 Sep — until M8 they are labelled pre-release). Updates'
  AB5 entry finished, landing page and HANDOFF updated.

## Critical files

New: `Diagnostics.cs`, `SafetyMarker.cs` (crash protection), `Experiments.cs` (flag
registry + env-arg builder), `FilterEngine.cs`, `Shields.cs`, `DownloadRing.cs`,
`DownloadsPopover.cs`, `FlyoutTheme.cs`, `TabGroups.cs`, bundled list data files.
Modified: `BrowserTab.cs` (request pipeline), `WebViewEnvironment.cs` (environment
options from `Experiments`), `App.xaml.cs` (crash hook → Diagnostics + SafetyMarker),
`MainWindow.xaml.cs` and partials, `InternalPages.cs` (`SECTIONS`), `AppSettings.cs`,
both theme files.

## Reuse, not rebuild

- `AppSettings` schema handshake — new keys register themselves; never reintroduce a list.
- `BuildInfo` gating — new rows carry `phase:5`, live when M8 bumps `PhaseShipped`.
- `PrivacyReport` already keeps per-day/per-tracker/per-site counts; the engine feeds it.
- `SiteSettings` (`MainWindow.Reader.cs`) is the per-site store for Shields and HTTPS
  exceptions.
- The debounced-writer pattern in `History` / `Favicons` for the Diagnostics writer.
- `Res<T>` for look-dependent geometry; both theme files keep an identical key set.

## Verification

- Every milestone: clean build, publish, launch the real exe, confirm foreground before
  any input, screenshot via the `front-and-shoot` / `click` / `type` scripts.
- Internal pages (Settings, Experimental, Privacy Report): extract and drive in the
  browser pane (HANDOFF §5).
- **Diagnostics end to end — every one of the 16 categories proven, not assumed**: switch
  on → folder and `manifest.json` appear; browse and search → `browsing.jsonl` has full
  URLs, titles and queries; load an ad-heavy page → `network.jsonl` and `blocking.jsonl`
  fill; load a page with a broken script → `pages.jsonl` records the error; trigger a
  swallowed exception → `issues.jsonl`; kill a renderer → an engine-failure entry; force a
  crash → `crashes/` gets a file with breadcrumbs and open tabs; `summary.md` regenerates;
  switch off → nothing further is written; cap and retention trim as designed.
- **Secrets never land on disk**: sign in to a test page and fill a form, then grep the
  whole folder for the password, the typed form values, cookie values and an OAuth `code=`
  value — **zero matches**, and masked parameters show `•••`.
- **Experimental safety**: turn on a flag, force a crash → next launch comes up with
  experiments off and the banner; "turn every experiment off" works; confirm no excluded
  security flag is reachable from the page or from `settings.json` editing.
- **Every experimental flag** is toggled and its effect observed; any that does nothing
  is cut.
- **Ad blocker**: before/after counts on ad-heavy sites; ad boxes collapse; Shields-off
  site loads unblocked; match cost from `perf.jsonl` within budget.
- **Privacy**: `Sec-GPC` present, referrers trimmed, tracking params stripped, third-party
  cookies refused, HTTPS upgraded, UA toggle flips what a UA-echo page reports.
- **Settings audit re-run** (the key-by-key audit from Context): zero live, inert rows.

## Honest notes

- The largest phase yet; M3 and M6 are each bigger than most earlier phases. Milestones
  are independent enough to stop cleanly between them if Grant's weekly limit bites.
- Some fingerprinting defences and the frame-rate cap may not be reachable through
  WebView2; those will say what they cover, or be cut, rather than overclaim.
