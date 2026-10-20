# M8 verification — 20 October 2026

Alpha Build 5, M8: measured optimisation and polish. Source now reports 15/16;
M9 remains pending real GitHub CI and final release. macOS is deferred.

## Measurements and practical limits

- M7 baseline: first Classic → Liquid Glass swap with 22 tabs, 187.9 ms;
  associated UI stall 141.6 ms.
- M8 scratch `m8-polish`: first Liquid Glass switch with two tabs 75.9 ms,
  UI stall 74.5 ms. With 22 open tabs plus one prepared tab, warm switches
  Classic 25.1 ms and Liquid Glass 24.1 ms. These warm values are not a
  like-for-like cold-start percentage claim.
- CDP read-only snapshots before/after show all 22 Start Page time origins
  unchanged. Their tile radius changes 18 → 14 → 18 px, with live palette styles.
  No new Start Page build occurs during the look swap.
- Rapid creation of 20 tabs still recorded a 72.8 ms wait under system load.
  Zero stalls over 50 ms is NOT achieved; preserve the Beta limitation.
- Filter harness: 125 passed; 200,000 requests each mode, average 2.08 µs Strict,
  2.01 µs Standard; worst 412.1/291.2 µs. Kept the all-context, all-source request
  filter because narrowing it would omit privacy handling for some requests.
- Memory saver recheck, `m8-sleep-audio`, renderer 36980: 64 arrays totalling
  67,108,864 bytes. Awake working set 102.0 MiB, private 93.8 MiB. After sleep,
  working set 14.84 MiB, private 88.45 MiB. It wakes and all 64 buffers still
  contain their expected bytes. Working-set trim is not private-allocation release.

## Implemented and exercised

- Theme swaps preserve tab controls and Start Page documents; pending navigations
  receive the current palette when complete. Document-token checks remain in place.
- Favicon and hover storyboard dictionaries remove completed/unloaded controls.
  Motion reveal tokens now use weak keys. Minute diagnostics show zero retained
  fade entries after normal hover/navigation activity. A long-duration leak soak is
  not claimed; the remaining many-tab release smoke will also inspect the counts.
- A 16 px loading ring is visible on `/slow`; it yields to the favicon after load.
  Reduced motion uses the site identity instead of an animated ring.
- Actual native audio tests against `/tone`: Windows Core Audio meter 0.02 playing,
  0 muted, 0.02 unmuted. The page keeps playing throughout.
- Background audio clicks leave Start Page selected. Verified with 22 crowded tabs
  and with a pinned tab. Sidebar controls work too.
- With automatic background mute enabled: backgrounding silences the tone; choosing
  Keep Tab Muted sets explicit mute; returning foreground stays silent; explicit
  unmute restores 0.02. Silent auto-muted tabs no longer need an audio button.
- Crowded-strip clipping found and fixed: the audio action precedes the shrinking
  title. Downloads has a useful empty-state explanation.
- Settings audit corrected to compare phase badges with actual PhaseShipped:
  99 live keyed rows, zero unread; 3 deferred keys (startup, translate, background).
  All 21 Experimental registry rows match. There are no AB5-only deferred keyed rows.
- Existing PiP script regression: 11 passed. Release build: zero warnings/errors.

## Release preparation so far

- SDK fixed to installed 9.0.317. NuGet lock generated and locked restore verified
  using only the existing local package cache. No new package downloads.
- Safe Collection sync: default retains old copies, incomplete prune fails before
  writes, repository-root and linked destinations rejected. Six scratch checks pass.
- Early packages were superseded by the final repair. Current candidates and all final
  package checksums are recorded in `M9-RELEASE-2026-10-21.md`. No commit or published-copy replacement.

Raw evidence is under ignored scratch profiles and .artifact/m8-*-look.json.
Native inputs in this session used the Computer Use sky APIs. Old PowerShell UIA
input harnesses were not rerun; historical results are not represented as new passes.

## Final stress repair and packages

First packaged Glass switch: 75.5 ms with 22 tabs (187.9 ms M7 baseline).
V2 measured 73.4 ms. Rapid tab churn exposed stale composition offsets; merely
stopping the spring was insufficient. Final cleanup commits the current XAML layout
position and uses weak generation keys to avoid an older timer clearing a newer glide.

V3 test `m8-layout-v3`: 20 rapid opens, all 21 visual positions match XAML (maximum
error 0.00003 px in the sampled diagnostic), zero tab/favicon fade cache entries.
20 rapid middle closes settle to one correctly positioned, full-width tab. Closed
normally. All prior v1/v2 packages are superseded and are not final candidates.

Current local/source package paths are in `M9-RELEASE-2026-10-21.md`; the 20 October
packages are superseded. Native full and source-only ZIP checks both passed on 21 October.
M9 still requires owner/repository,
visibility, account connection, approved first upload and actual successful GitHub CI.
