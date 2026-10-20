# M7 Super Quik — verification, 20 October 2026

Local Windows x64 Release build, .NET SDK 9.0.317. All launches used explicit scratch
`AVIARY_PROFILE_DIR` paths under `tools/test/profiles`. The existing unfinished M7 work was
preserved in `.artifact/m7-before-20261020` before editing. No new packages or filter lists.

## Measured results

Classic, same warmed `m7-benchmark` profile, alternating regular / Quik, normal closes,
news off in both modes, diagnostics on in both. Process start to first page loaded:

| Mode | Ten startup measurements (ms) | Median |
|---|---|---|
| Regular | 970.2, 931.9, 903.0, 924.7, 975.2, 984.4, 948.5, 975.0, 1008.1, 1012.1 | 972.60 ms |
| Quik | 720.9, 700.4, 678.2, 779.3, 807.3, 749.6, 710.9, 713.1, 759.2, 823.7 | 735.25 ms |

**24.4% faster** in this controlled Classic test. This is not a guarantee for a disk-cold
launch, another PC, Liquid Glass, or network page loads. Before the final change, three
pairs measured only 17.09% faster. Startup phases showed 12–21 ms spent making hidden
Liquid Glass decorations in Classic; Quik now makes them on the first actual look switch.
The earlier records remain beside the final logs as `*.before-lazy`.

Twenty separate Ctrl+T opens, with the ordinary one-tab Quik pool replenished between
opens: **median 8.305 ms**, range **6.78–11.57 ms**, all twenty recorded `ready:true`.
This measures handing an already loaded page to the visible tab, rather than a new
network document's first paint. Ordinary mode's two initial opens measured 264.36 and
210.91 ms. For a two-click burst, ordinary Quik measured 10.01 ms prepared / 168.08 ms
ordinary; Quik Labs' two-tab pool measured 9.96 / 6.07 ms, both prepared.

Reproduce summaries with `node tools/test/m7-metrics.js m7-benchmark`. Raw scratch logs
are in that profile's `user.data.improvementcollection`. They are local test evidence,
not committed browsing data. `.artifact/m7-final-metrics.json` also holds the summary.

## Functional checks in the running app

- PiP offered through a transparent player overlay; entered successfully, fixture reported
  PiP true, and Computer Use enumerated a separate native “Picture in picture” window.
  Aa offered Leave Picture in Picture and successfully returned the video to the page.
- Settings → background → Your picture opened the real Windows picker. Escape restored
  Glasshouse in the visible row and store. A second profile started with a copied test
  image; choosing Dawn changed the row/store and removed `startbackground.img`.
- The open Forgetful sites pane showed a seeded `m7.test` site due in one minute, then
  changed to “closed, and forgotten” without refreshing or reopening Settings. The native
  sweep changed `sites.json` from `forgetAt` to `forgot:1`.
- Turning Keep a new tab ready off reduced the engine's page targets from four to two
  (the two visible tabs); turning it back on rebuilt both Labs pages.
- A copied-page new tab opened the same localhost page in a separate tab. This exposed
  and fixed an older bug: the source URL must be captured before selecting the empty tab.
- After a new engine was created, the background localhost page reported hidden while
  the foreground page reported visible; Reader's page script was present.
- A prepared Start Page accepted an actual Customise button click after switching from
  Classic to Liquid Glass. The popover showed Bird News and the background choices.
- Updates' rendered DOM contained the M7 rename note and the Set up Quik button.
- With a simulated unclean-run marker in `m7-safety`, launch switched both Quik and the
  new Labs key off before creating the engine. The native recovery dialog named both;
  Keep Them Off dismissed it. This specifically checks the new raw-setting reset path.
- In that recovered regular launch, Quik reported off and its prepared-tab, recent-tab
  and Labs switches were disabled. The final Updates payload reported 14 milestones
  complete and contained the explicit Bird News rename note.

## Automated checks

- Release build: successful, no errors. Existing CS0414 warning for `Favicons._indexDirty`
  appears twice because the XAML build compiles twice.
- `settings-audit.js`: 97 live keyed rows, five gated, zero unread; 21 experiments with
  zero registry mismatches. The registry parser now handles the Labs entry without
  accidentally treating the following engine experiment as inert.
- `m7-script-test.js`: 11 passed, including overlays, unrelated page areas, Aa playing and
  paused cases, site opt-out, missing metadata, engine disabled, enter/leave/refusal.
- Filter harness: 125 passed, zero failed; bundled lists loaded and matched correctly.
- Search harness earlier in this work: 54 offline checks passed, zero failed. Online
  suggestions answered for five of six engines; Startpage returned no suggestions.

## Limits and remaining release work

Two UI stalls were recorded in the final benchmark profile: 55.5 ms during system load,
and 141.6 ms while switching looks with 22 tabs (the whole look swap took 187.9 ms).
No claim of every operation taking 10–20 ms, or zero stalls. The many-tab look switch is
listed in Quik's Beta limits and remains an M8 optimisation candidate.

Prepared tabs consume memory and can run out. Links and copied-page tabs use the ordinary
path. Measurements of the new feature do not replace every historical regression suite:
the old PowerShell harnesses that send native UI input were not rerun in this Computer
Use session. Earlier stage 1 intro/Restart checks are historical evidence in HANDOFF §28.

The completion changes are uncommitted. The existing `publish/Aviary` has not been
replaced. Fresh executable: `src/Aviary/bin/x64/Release/net9.0-windows10.0.19041.0/win-x64/Aviary.exe`.
macOS support is explicitly deferred.

Final rebuilt binary smoke (`m7-final-smoke`): Start Page and prepared pool loaded,
Settings → Updates rendered **14 of 16**, Next M8, M7 dated 20 Oct with the explicit rename,
Set up Quik, the measured numbers, and the many-tab look-switch limit. Closed normally.
