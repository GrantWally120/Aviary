# Aviary — independent playtest review
**Build:** 0.5.0 · Alpha Build 5 "The Long Run", newest dev build (`src/Aviary/bin/.../win-x64`, built 21 Sep 2026 17:39, uncommitted M4.9 Stage 3 work included) · WebView2 153.0.4234.48
**Reviewed:** 23 Sep 2026, about 2.5 hours of hands-on play on Grant's PC (9800X3D, 64 GB, RTX 5070 Ti, 2560×1440 @ 240 Hz)
**Profile:** a fresh test profile (`tools/test/profiles/review-2026-09-23/`, launched by `launch-review.bat` there), with diagnostics switched on. Grant's real profile was also opened briefly three times by the test tool.

## Verdict: 7 / 10 (as an alpha)

It already looks and feels like a real, premium browser, and under the hood it is solid: zero crashes, 240 fps, and the same speed as Brave on the same engine. What holds the score back is everyday handling: YouTube breaks with the default settings, keyboard shortcuts get lost while pages load, and the address bar fights you. Fix the top five bugs below and this is an 8.

| Area | Score | One line |
|---|---|---|
| Design and polish | 9 | Liquid Glass and Classic both look intentional; live theme swaps in ~86 ms |
| Stability | 9 | 0 crashes, 0 swallowed errors, 4 UI stalls of 58–109 ms in 2.5 h |
| Performance | 8.5 | Speedometer 3.1 = 40.6 ± 2.3 vs Brave 39.8 ± 2.4; 239 fps at 240 Hz; 1.37 s start |
| Privacy and security | 8 | Real Shields, HTTPS-Only, GPC, punycode-safe, phishing blocked; a few gaps |
| Extras | 8 | The Collection, Birds News, Performance, Storage, Safety Check, Search Tabs |
| Web compatibility | 5 | YouTube playback dies at 25–50 s with "Identify as Safari" on (the default) |
| Core browsing and keyboard | 5 | Lost shortcuts, autocorrect in the address bar, no Esc/F5/Alt+←/print |

## Bugs found (most serious first)
Every item was reproduced at least twice unless it says otherwise.

1. **YouTube stops at 25–50 s: "Something went wrong. Refresh or try again later."** Big Buck Bunny 4K60. With *Identify as Safari* ON it failed 3/3, including with Shields OFF for youtube.com. With it OFF, it played 95 s+ in 2/2, with Shields Off and with Standard. So the Safari identity is the cause, not the ad blocker. The UA also still says `Version/18.0`, but Safari 27 is current (Sep 2026). **Fix:** a site-compatibility list that sends the plain Chromium identity to YouTube (and a per-site "Use Safari identity" switch in the shield popover). Bumping the UA alone is unlikely to be enough.
2. **Keyboard shortcuts are lost while a page is loading.** Opening tabs quickly (type URL, Enter, Ctrl+T, Ctrl+L, type), Ctrl+T was lost 3 times in 18. Two tabs were overwritten, and once Ctrl+T and Ctrl+L were both lost, so the typed address and Enter went **into the ESPN web page**. `events.jsonl` has no shortcut event for those presses. **Cause:** `AreBrowserAcceleratorKeysEnabled = false`, and shortcuts depend on the injected page script, which is not running between navigation start and document creation. **Fix:** catch shortcuts before the page does (the WebView2 controller's `AcceleratorKeyPressed` if reachable, or a thread keyboard hook), not in page JS.
3. **The address bar autocorrects and spell-checks.** Diagnostics recorded `github.com` typed as `GitHub.com`, `nytimes.com` as `NYTimes.com`, `microsoft.com` as `Microsoft.com`. Red squiggles appear under search words. **Fix:** `IsSpellCheckEnabled="False" IsTextPredictionEnabled="False"` and `InputScope="Url"` on `AddressBox` (MainWindow.xaml ~L606).
4. **A new tab does not focus the address bar.** Ctrl+T or + gives a Start Page, and typing goes nowhere. **Fix:** after `NavigateToHtml(StartPage)`, focus `AddressBox` with `FocusState.Keyboard` instead of `FocusPage(tab)` (MainWindow.xaml.cs ~L765).
5. **Escape does nothing.** It doesn't close the suggestions or restore the URL in the address bar, and it doesn't close the Find bar or Aviary's context menus. `HandleSuggestionKey` has an Escape case, but it never fires. Likely fix: register with `AddHandler(KeyDownEvent, …, handledEventsToo: true)` or PreviewKeyDown.
6. **Liquid Glass tab strip overlap.** With the Fluid motion style and "New tab: after the current tab", a tab inserted mid-strip gets no slot of its own. It overlaps its neighbours ("Settings M This page didn't load Start Page") and stays broken after switching tabs or closing it. 2/2. Switching to Classic re-lays it out, and Classic never shows the problem.
7. **Leaving Reader breaks the page.** `Reader.cs` restores `documentElement.innerHTML`, so every script listener is gone. On Wikipedia afterwards, the Dark/Light radios and "hide" did nothing until a real reload. **Fix:** reload on exit, or draw Reader in an overlay or shadow root. Reader also keeps Wikipedia's hatnotes, coordinates and the whole infobox gallery.
8. **A typed download link stays in the address bar.** Typing a .zip URL on the Speedometer tab downloaded the file, but the address bar kept showing `github.com` over browserbench.org, even after switching tabs. `BrowserTab.Navigate()` sets `Url` before commit, and a download never commits. Bookmark or Reading List would then save the wrong URL. Page-initiated navigations are not affected, because they go through `SourceChanged`.
9. **A stray "Ctrl+T" tooltip floats over pages** when the mouse rests anywhere (seen 4×). All accelerators sit on `RootGrid` with the default placement. **Fix:** `RootGrid.KeyboardAcceleratorPlacementMode = Hidden`.
10. **Blocked phishing looks like a network error.** SmartScreen blocked `demo.smartscreen.msft.net/phishingdemo.html` ("Reported Unsafe Site"), but Aviary covered it with "This page didn't load (Unknown)". The user is protected but never told why. Show a real "Deceptive site" warning.
11. **Local files can't be opened.** `Navigate()` accepts only http/https. `file:///C:/…/Aviary.html` becomes `https://file:///…` ("address doesn't exist", with the address bar showing "file"). Windows paths with spaces become a search, and there is no Ctrl+O. `mailto:x@example.com` would become `https://mailto:x@example.com`.
12. **Missing everyday shortcuts and actions:** F5, Alt+←/→, Ctrl+Shift+T (reopen a closed tab), Ctrl+P and any Print item, F3, Ctrl+S, F12. There is no "New Window" (Ctrl+N), and the image menu has no Save or Copy Image.
13. **Ctrl+click on a link replaces the current page, and middle-click does nothing.** "Open Link in New Tab" also jumps to the new tab. (LinkScript is planned for M5.)
14. **Full screen out of a Snapped window** (F11 or video full screen) restores the old floating size, not the snap.
15. **HTTP pages have no "Not Secure" label** after Continue, only a missing lock. The HTTPS-Only fallback also takes 5–8 s of blank page before the panel shows.
16. **The cert-error panel has no Go Back button** (expired.badssl.com).
17. **Permission prompts and JS dialogs use WebView2's stock Chromium UI.** There are no `PermissionRequested` or `ScriptDialogOpening` handlers, and no per-site permissions page.
18. **Safety Check is misleading on a new install.** It says "filter lists are up to date" while also saying they were updated 10 days ago. It doesn't cover SmartScreen, passwords or updates.
19. **Switching the tab layout back to Top bar leaves the sidebar open.** Sidebar mode has no + button for new tabs.
20. **Frequently Visited on the real profile** shows "(145) YouTube", with the notification count kept in the title. It lists both "The Collection" and the old "The Menagerie", and it includes search-result pages as tiles.
21. **Minor:** low-contrast placeholder and inactive tab text; the private window looks almost the same as a normal one; the Downloads flyout has no progress, size or cancel (M5). *Unconfirmed:* after closing the YouTube tab, a 341 MB "Pages" process with no tab was still listed. Worth checking for a renderer that isn't released.

## What works really well
Every item here was tested and working.
- **Looks:** Classic and Liquid Glass, Light and Dark, all switch live. The Start Page, Settings and the Collection look designed, not generated.
- **Shields:** the per-site Off/Standard/Strict popover lists what was blocked, the rule and the list (Inquirer: 4 blocked, no ad boxes). The request check costs about 6 µs.
- **Privacy:** HTTPS-Only with a clear, plain-English panel; the IDN homograph shows as punycode; GPC and third-party cookie blocking are on; the private window's banner is clear.
- **Close guard:** "27 tabs are open…" before closing. Search Tabs (Ctrl+Shift+A) works. Tab drag works in the sidebar.
- **Performance page:** Task-Manager quality, with per-tab memory and Sleep/Close buttons. With 27 real sites open: 3.12 GB in use (peak 8.32 GB), CPU 1.4 %, and the window answering in 0.1–0.4 ms.
- **Speed:** 1.37 s cold start, about 130 ms to start a new tab's engine, page-load median about 1 s, 239 fps in TestUFO at 240 Hz, and AV1 4K60 video in full screen with 4 of 3,998 frames dropped.
- **Birds News:** fresh headlines within seconds of launch, updated live on the page.
- **The Collection:** Wally2TTD 2.4.2 (Godot, 38 MB) loaded in about 8 s and played wave 1 with turrets firing.
- **Downloads:** the file saved and "Open Downloads folder" selected it.

## What to add next (in my priority order)
1. A site-compatibility list and a per-site Safari-identity switch (YouTube first).
2. Session restore and reopen closed tab (Ctrl+Shift+T). Losing 27 tabs on close is the biggest daily-driver gap.
3. Open local files: file://, drag-and-drop, Ctrl+O. You build HTML games, so your own browser should open them.
4. Print (Ctrl+P), F5, Alt+←/→, and Ctrl/middle-click to open links in background tabs.
5. Aviary-styled permission prompts and JS dialogs, plus a Site Settings page (camera, mic, location, notifications, pop-ups).
6. The download ring and manager, and tab groups (M5); Picture-in-Picture and Start Page backgrounds (Stage 3).
7. A "Not Secure" label, a real deceptive-site page, and a Go Back button on every error panel.
8. Birds News: let the user add feeds. A Cebu source (for example SunStar Cebu or CDN Digital) would make it feel local.
9. Tab hover previews and an audio indicator with mute (already on the M6/M7 list).

## How this was tested
Real sites: Wikipedia, DuckDuckGo, YouTube, Inquirer, Rappler, CNN, The Verge, Reddit, GitHub, BBC, Twitch, Apple, ESPN, NYT, weather.com, X, Microsoft, Stack Overflow, Lazada, TestUFO, Speedometer 3.1, neverssl, badssl, the SmartScreen demo, and an IDN homograph test. Also exercised: Reader, Find, zoom, bookmarks, the sidebar, history, the private window, F11 and video full screen, snapping, themes, Shields modes, a download, 27 tabs, and the Collection game. Evidence is in the test profile's `user.data.improvementcollection` (summary.md, events.jsonl, issues.jsonl). Brave benchmark: Chromium 153, a smaller window, one run each, so treat the speed result as "same speed", not a winner.
