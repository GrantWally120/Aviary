# Settings accuracy and live details — 21 October 2026

Requested after completed AB5: make the AB5 badge expand all milestones; improve expanded or selected Performance details, metric accuracy, live Storage and Settings efficiency/bugs. This is a follow-up to AB5, with its 16/16 release counters preserved.

## Result applied to the ordinary local copy

Current tested candidate: `.artifact/ab5-settings-mb-20261021/Aviary`, version 0.5.0.0, 662 verified files, 54/54 exhibits. Its `manifest.json` records every file hash. Grant explicitly requested fixing his ordinary copy, so the exact candidate was applied to `publish/Aviary` at 20:27 +08 and reopened. All installed hashes match. No dependency or lockfile changes. Collection sync copied 0, retained 90 unchanged and removed 0; the unavailable source pattern retained existing copies.

The prior candidate `.artifact/ab5-settings-live-final-20261021` has the completed live Settings but binary display. `.artifact/ab5-settings-live-20261021` also predates final card layout, focus handling, notes and the atomic Shields counter correction. Both and the dropdown-only candidate are superseded; do not install them.

## UI and measurement review

| Before | After | Why |
| --- | --- | --- |
| Released AB5 hid its milestone list | Build header/AB5 badge is a native disclosure with all 16 dates/descriptions | Completed work stays accessible by click or keyboard |
| Live tables/categories were rebuilt | Rows and controls retain their nodes; closed process/tab details populate on demand | Preserve expanded state, selection/focus and reduce DOM work |
| Sorting appended graph samples | Sorting only renders; new timestamped host samples enter the 60-second graphs | Avoid fabricated activity/history |
| GPU engine detail included unrelated processes and totals summed parallel maxima | Filter Aviary PIDs, combine a physical engine's work, then use the busiest engine | GPU percentages are not additive across parallel engines |
| Warming or unreadable counters looked idle | Explicit unavailable/partial readings and baseline descriptions | A missing measurement is not a measured zero |
| Storage totals/site breakdown could use different file reads | Length/allocation/site/category values share one metadata sample | Keep the measured categories internally consistent |
| Storage silently omitted failures and rounded allocation | Show incomplete scans/estimates and query Windows allocation | Account for sparse/compressed files and access failures honestly |
| Polling and async starts could outlive their page/window | Central pane/visibility lifecycle, bounded delivery queues and per-window ownership | Stop background work promptly and prevent stale callbacks |

Memory is private working set, with private commit/total working set in details. At Grant's latest request, sizes automatically switch between B/KB/MB/GB/TB, using decimal conversion (1 KB = 1,000 bytes, 1 MB = 1,000,000 bytes). Performance retains byte precision through its existing binary internal fields, converting back to bytes before formatting; rounding occurs only for display. Website data and the Experimental overlay use matching units/calculation. Peak aggregate is explicitly a sum of separate current-process lifetime peaks, not a simultaneous peak. Tab values cover its main renderer process, can be shared, and do not include every out-of-process frame/browser cost. Dedicated GPU allocation can count allocations shared between processes more than once; it is not whole-card physical occupancy. CPU uses actual elapsed time and all logical processors. Shields request count/time/maximum move between windows atomically.

Storage covers this installation and persistent profile only. Other copies, external downloads, shared WebView runtime, directory metadata and alternate streams are excluded. Hard-linked paths retain logical lengths while shared disk allocation is counted once. A snapshot spans a scan, so live files can change during it; skipped entries and estimated allocation are exposed. Polling waits 2–10 seconds after completion according to scan cost and pauses while hidden/elsewhere. This is live periodic measurement, not an atomic filesystem image.

## Validation

- Combined app build and final publish succeeded. The explicit Release build reported zero warnings/errors; final folder verification passed.
- Performance harness: **26 passed**, including physical GPU engine aggregation, CPU interval normalization, real Windows memory, unavailable/warming counters, retiming, listener ownership, rapid start/stop, and 200,000 requests from 8 producers racing atomic Shields snapshots.
- Storage harness: **18 passed**, including category/site reconciliation, changing IndexedDB files, top-site remainder, Windows casing, overlapping/equal roots, cancellation, missing roots, actual sparse/resident allocation, hard links, junction exclusion and coalesced requests.
- Separate NTFS compressed fixture: 4 MiB logical data used 512 KiB; FileStandardInfo, FileCompressionInfo and GetCompressedFileSize agreed. Fixture removed.
- Existing checks: release-state **8**, player **11**, Collection availability **7**. Settings audit: **99 live keyed rows**, none unread; **21** experimental rows match the registry.
- Both measurement harnesses are now included in the Windows workflow. No real GitHub CI has run for this uncommitted follow-up; prior AB5 CI remains historical evidence only.

## Native QA

Tests used explicit scratch profiles. Native input used sky when the desktop guard reported idle. Read-only DevTools inspection targeted isolated ports 9431/9432/9433; no navigation/input over DevTools. Cache-clear controls were focused, never activated. After scratch tests, the user-requested normal update was applied and its Settings pages were opened for the user; no normal-profile cache/data was cleared and no normal-profile debug port was enabled.

- Initial QA created/removed one 4 MiB file in its own scratch profile. Live totals grew/reduced automatically (other browser files were also changing), all category bytes reconciled, and expanded Website data remained open.
- Performance tab/process/card disclosures displayed real private/committed/total/peak memory and GPU/CPU values. Keyboard sorting retained the expanded tab and focus; chart timestamps stayed unique. Selecting About stopped both page watchers and their sample timestamps remained unchanged.
- Final QA verified the native AB5 dropdown, live Storage, and keyboard focus on its Clear cached files button across automatic refreshes, without clearing anything.
- Final Performance showed 8/8 processes readable, 0.999-second actual window, 0.94 ms last sampler cost and 60 unique graph samples. These are single observations on this PC.
- Final card layout gives explanations more room and avoids stretching all metric cards to the open card's height. Expanded tab/process facts are lazy and retain stable controls.
- Both QA sessions ended `Aviary closed`, with **0 crashes, 0 issues**. Closing the final window while Performance was active exercised native watcher shutdown. No QA process remains.

Artifacts: final folder contains `final-native-state.json`, `storage-focus-before/after.json`, `storage-expanded-native.png`, `performance-expanded-native.png`, `ab5-milestones-native.png`, manifest and build/sync logs. Initial QA observations and detailed process/table screenshots remain in `.artifact/ab5-settings-live-20261021`.

## Decimal-unit verification and local application

- After the requested precision/unit change, the new Release publish succeeded. All **26 performance** checks, **8 release-state** checks and the **99-row Settings audit** passed again.
- **11** independent size boundary cases checked 0/999 B, decimal KB/MB thresholds, the former 1,024/1,048,576-byte boundaries, near-threshold rounding and GB/TB. The actual Storage and Performance formatters agreed; unknown/invalid readings remained unavailable. No binary unit labels remain in Settings.
- Fresh native profile `ab5-settings-mb-qa-20261021` showed 66.5 KB with exactly 66,547 bytes in Storage details, and 345.7 MB from the full-precision 345,673,728-byte private-resident sample. Categories reconciled and live samples advanced. It closed normally (`Aviary closed`), **0 crashes / 0 issues**; no QA process remains.
- Grant's ordinary old process (36672) was closed normally with only Start Page and Settings open. Its whole install folder was preserved at `.artifact/ab5-published-before-settings-mb-20261021`. A verified temporary copy was activated; all **662** installed hashes match the tested manifest, with all **54** exhibits available.
- Reopened ordinary Aviary (38968), verified `E:\Claude CoWork Enclosed\Zoo\publish\Aviary\Aviary.exe`, and verified Desktop/taskbar shortcuts both target it with no special arguments. Its visible Storage showed 601.5 MB and live measurements; AB5 expanded all 16 milestones; new Performance details displayed decimal MB. Left Performance open for the user.
- Evidence: new candidate folder's `verification.json`, `storage-native-state.json`, `performance-native-state.json`, `applied-local.json`, `storage-decimal-native.png`, `performance-decimal-native.png`, and `ordinary-storage-native.png`, `ordinary-ab5-milestones-native.png`, `ordinary-performance-native.png`.

Source is still uncommitted/unpushed, and this follow-up has no actual GitHub CI run. No new commit/push approval has been given. Local application is authorized and complete; do not ask for it again. Personal Collection payloads remain local. The completed AB5 continuation remains paused.
