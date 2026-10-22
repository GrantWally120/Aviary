# Aviary: review of Codex's AB5 work, and the fixes that followed
**Reviewed:** 22 Oct 2026, by Claude (Sonnet 5.5), at Grant's request · **Build reviewed:** the installed 0.5.0 "The Long Run" (`publish/Aviary`, source 102abec, 663 files) · **Source head:** d41182f
**How:** read the handoff and cleanup records, hashed every installed file, ran every test and harness, built the source, launched the app on scratch profiles and used it by hand (Start Page, Settings, Collection, a Godot game, real sites, YouTube), read Codex's newest security code, and compared the app with the 23 Sep playtest review that sits in this folder.

## Verdict
Codex delivered what its records say it did, and the engineering is careful. The installed app is intact, every automated check passes, and the privacy and security work is real and well tested. What it missed is the other half of the job: the 23 Sep review's findings were never acted on (they are not mentioned anywhere in `HANDOFF.md`), so the app still breaks YouTube by default and still lacks basic keys. Its 22 Oct cleanup also quietly broke the test kit. I fixed both, plus a stale test and some stale docs. The fixes are in source and in a tested build. **Update, same evening:** Grant approved, so the build is now installed in `publish/Aviary` (663 files hash-checked, 18 of 18 smoke checks, and the tooltip, spell-check and Escape fixes confirmed by eye) and the changes are committed locally, not pushed. The old build is the rollback ZIP in `Backup`.

## What I verified (Codex's claims that hold)
- **Installed app:** all 663 files in `publish/Aviary` match the manifest hash-for-hash; no extras. Version 0.5.0, source 102abec.
- **Recovery ZIP:** `Backup/Aviary-AB5-local-win-x64.zip` matches its SHA-256 (b0420c97…e9d3) and holds 671 entries.
- **Collection:** 54 of 54 exhibits; all 161 files under `Library` are byte-identical between source and installed copy; the Godot 2.4.2 port and Wally2TTD v1.9 both play inside Aviary.
- **Tests:** filters 125, security 112, Performance 30, Storage 18, plus the settings audit (100 live rows, none unread), player 11, availability 7, release-state 8, diagnostics script, and library-sync 6. All pass. The source builds with 0 warnings and 0 errors.
- **CI:** Actions run 39630732602 really did succeed on 102abec (checked on GitHub). Git is in sync with `origin/main`.
- **Shortcuts:** the root `Aviary.lnk`, the Desktop one and the taskbar one all point at `publish/Aviary/Aviary.exe`.
- **Cleanup:** the 83 removed targets were redundant app copies, 22 duplicate ZIPs, rebuildable bin/obj and scratch profiles; nothing unique appears to have been lost (by its receipts, the cleanup hashed every historical Collection file first and kept the three unique ones, which are in `Backup/Collection-history`).

## What Codex did well
- **The security code is tight.** `SecurityPolicy`, `BoundedDownload`, `DocumentScriptSlot` and `IconDownload` are small, bounded, testable and commented with the reason for each limit. Messages are size-capped and depth-capped, filter downloads are pinned to two hosts and streamed with a hard byte cap, permission prompts are denied on insecure origins, scripts cannot launch external apps.
- **Honest self-reporting in the product.** Safety Check told me, correctly, that the filter lists were 39 days old, with a one-click fix. Storage and Performance explain what they cannot measure instead of faking a number.
- **Verification culture.** Every release has a manifest, hashes, a checksummed ZIP, and a smoke test; the cleanup kept receipts for everything it removed.
- **The look and feel.** Classic and Liquid Glass both switch live, the Collection page is genuinely designed, and the Start Page is calm and fast.

## Findings in Codex's work
### Broken (fixed)
1. **The cleanup broke the test kit.** It deleted `tools/test/pub`, the dev build that 26 test scripts default to, so `launch-quiet.ps1`, `native-crash.ps1`, `quik-test.ps1` and everything built on them failed unless given `-Exe`. The test README also still hard-coded the retired `C:\Users\user\Documents\...` path. *Fixed:* a small `tools/test/default-exe.ps1` makes the PID-driven scripts fall back to the installed `publish/Aviary` copy; the five scripts that find and kill their instances by path (`launch-m2`, `min-window`, `si-race`, `si-race2`, `si-test`) now stop with a clear "build the dev copy" message instead of ever touching the copy Grant may be using. Verified: `launch-quiet.ps1` with no arguments launches the installed exe on a scratch profile.
2. **`publish-smoke.ps1` was stale.** It still expected "12 of 15 milestones done" from M5 and failed on the shipped 16/16 build. *Fixed* to check that Updates names the shipped build; it now passes 18 of 18.

### Should fix (docs fixed, rest recommended)
3. **README contradicted itself.** It said the Settings work was uncommitted and that the Performance/Storage CI steps "have not yet run remotely". Both are false (committed at 5c94840 and following; run 39630732602 passed with them). *Fixed.* The CI-matching `diagnostics-script-test.js` was also missing from the build list. *Fixed.*
4. **The handoff documents are hard to use.** `HANDOFF.md` is 293 KB and 3,160 lines, opening with six stacked status banners (three headed "LATEST", one "CURRENT"), several describing candidates that no longer exist. A new session has to read all of it to know what is true. *Not changed:* it deserves a deliberate consolidation, ideally by whoever owns it next.
5. **The 23 Sep review was never triaged.** See the table below; most of it was still open.
6. **Maintenance Codex deferred correctly but left hanging.** The bundled filter lists are dated 13 Sep and auto-update is Off by default (so ad and tracker blocking is 39 days stale), and Windows App SDK 1.7 is out of support. The proposal is in `docs/SECURITY-MAINTENANCE-PROPOSAL.md` and needs Grant's yes. Until then, **Settings → Safety Check → "Update them now"** is one click.

### Polish
7. One rollback copy only: after the cleanup, going back to an earlier build means rebuilding from git. That was Grant's request, so this is a note, not a complaint.
8. Settings opens a new tab every time instead of reusing the one that is open.

## The 23 Sep playtest review: where each finding stands today
| # | Finding | Status now |
|---|---|---|
| 1 | YouTube dies after 25–50 s with "Identify as Safari" on (the default) | **Fixed in candidate.** Reproduced on the installed build: stops at ~35–45 s. With the identity off it plays 90 s+. Candidate plays 100 s+ on default settings. |
| 2 | Shortcuts lost while a page is loading | Open. Needs the engine's accelerator hook, which WinUI 3 hides; a design problem, not a quick fix. |
| 3 | Address bar autocorrects and spell-checks | **Fixed in code** (spell check and text prediction off, URL input scope). Not checked by eye. |
| 4 | New tab does not focus the address bar | **Fixed, verified** before/after: installed `focus=False`, candidate `focus=True`. |
| 5 | Escape does nothing in the address bar | Partly fixed. The first Escape already closed suggestions; the second now restores the address, a third returns to the page. Not exercised by hand. |
| 6 | Liquid Glass tab strip overlap (Fluid motion) | Not re-tested. |
| 7 | Leaving Reader breaks the page (restores `innerHTML`) | Open (`Reader.cs`, unchanged). |
| 8 | A typed download link stays in the address bar | Likely open (`BrowserTab.Navigate` still sets the address before commit). |
| 9 | Stray "Ctrl+T" tooltip over pages | **Fixed in code**; a "Ctrl+T" box appeared over a Start Page tile where the mouse rested, before the fix. Not re-checked by eye. |
| 10 | Blocked phishing page looks like a network error | Open (no deceptive-site panel in the source). |
| 11 | Local files / `file:///` cannot be opened | Open. Worth doing: Grant builds HTML games. |
| 12 | Missing everyday shortcuts | **Mostly fixed:** F5, Ctrl+F5, Alt+←/→, F3 / Shift+F3 / Ctrl+G, Ctrl+P, Ctrl+Shift+T (reopen), Ctrl+N (new window, also in the menu). Still missing: Ctrl+S, F12/DevTools, Ctrl+O. |
| 13 | Ctrl/middle-click on links | Addressed by M5 per the handoff; not re-tested. |
| 14 | Full screen out of a snapped window | Not re-tested. |
| 15 | No "Not Secure" label on HTTP pages | Open. |
| 16 | Cert-error panel has no Go Back | Appears addressed (Notices has a Go Back button); not re-tested. |
| 17 | Permission prompts use the engine's stock UI | Open by design: Codex kept the engine prompt, secure origins only. |
| 18 | Safety Check misleading | Improved: it correctly flagged stale lists today. |
| 19–21 | Sidebar left open, Frequently Visited oddities, minor items | Not re-tested. |

## The fixes (source changed, uncommitted; candidate built and tested)
- **YouTube** (`SafariIdentity.cs`, `BrowserTab.Privacy.cs`): YouTube is always shown the engine's own identity, whatever the Safari switch says; every other site still gets Safari. The identity can only change before a request goes out, so it uses the same cancel-and-re-issue step the privacy code already uses, never for Back/Forward/Reload, and the Settings text explains the exception. Verified in both directions (Safari → YouTube → Safari…), and by playing 100 s+ of video. Test hook: 16 new checks in the security harness (112 → 128).
- **Keyboard and address bar** (`MainWindow.xaml(.cs)`, `BrowserTab.cs`): the keys in finding 12, page-friendly "soft" keys (a web app that handles F5 or Ctrl+P itself keeps them), new-tab focus, Escape, no spell-check, no stray tooltip, and a "New Window" menu item.
- **Test kit and docs:** findings 1–3 above.
- **Verification:** build 0 warnings; filters 125, security 128, Performance 30, Storage 18, and all node checks pass; the 18-check publish smoke test passes on the candidate (profile closes cleanly, no issues, no crashes). Shortcuts were checked with trusted key events and the app's own diagnostics log; focus with UI Automation.

## What I could not verify
- **By eye (resolved after install):** the hidden tooltip, the address-bar spell-check change, and the Escape behaviour. Desktop control of the candidate window was declined, so I first tested through the app's own hooks; after Grant approved the install I checked all three on the installed copy using the grant already in place: no stray tooltip after 6 s of hovering, no red squiggles under "helo wrld", Escape closes suggestions then restores the address.
- **Printing** reaches the engine's print call and logs cleanly, but I did not print.
- **YouTube embedded in other sites** (an iframe) still gets the Safari identity; only top-level YouTube is fixed.
- **A blank page on my very first launch** (one of three on-screen launches; the engine itself had rendered the page). I could not reproduce it and believe it came from my own screen-capture setup, but I am noting it rather than dismissing it.
- I did not re-test items 6, 8, 14, 16, 19–21, or the Collection's other 52 exhibits.

## Recommended next steps
1. ~~Install the tested candidate and commit~~ Done on 22 Oct with Grant's yes (installed; committed locally). **Push** still needs Grant's approval, and CI has not run on the new commit.
2. Press **Update them now** in Safety Check, or approve the maintenance proposal.
3. Next product work, in order: local files (`file:///`, Ctrl+O), the Reader exit bug, "Not Secure" and a real phishing page, then shortcuts while loading.
4. Consolidate `HANDOFF.md` into one current page and move the history into an archive file.
