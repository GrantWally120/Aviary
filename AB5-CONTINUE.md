# Aviary AB5 continuation checkpoint — 21 October 2026

## Status: local work committed and published; GitHub connection pending

User authorization: finish the remaining AB5 milestones and continue after usage resets.
Preserve M7; macOS is deferred. Use this chat's current model settings.

- M7 Super Quik and M8 measured optimisation/polish are implemented and tested.
- BuildInfo: version 0.5.0, MilestonesDone=15 of 16, PhaseShipped=4, pre-release.
- M9 local preparation and candidate validation are complete. The required real GitHub
  CI run has NOT occurred. Do not mark M9 complete or AB5 shipped yet.
- Grant approved the M7/M8 commits, M9-preparation commit and local replacement on
  21 October. M7 is f427fe0; M8 is 42c2671. M9 preparation is recorded with this checkpoint.
- Grant supplied https://github.com/GrantWally120/Aviary.git and explicitly approved
  its existing PUBLIC visibility for the source/catalog upload. Do not ask again.
  The repository was verified empty. GitHub plugin/account connection is still pending.
- publish/Aviary now contains the exact tested 662-file local candidate. Every copied
  file matches the candidate manifest. The previous published copy is preserved at
  .artifact/ab5-published-before-approved-20261021. No app was running during replacement.

## Current review artifacts (all earlier package folders are superseded)

- Local: .artifact/ab5-local-synced-20261021/Aviary-AB5-RC-local-win-x64.zip
  662 files, 54/54 Collection exhibits, about 103.1 MiB.
  SHA256 e1cc0082071407bf51179134d3e229ede7eeba0b2d2ae97ca7853472d28ef624
- Source-only: .artifact/ab5-source-candidate-20261021/Aviary-AB5-RC-source-win-x64.zip
  508 files, four available landing pages; personal files/thumbs/game exports excluded.
  SHA256 dad2bd7f48dc1e4943b1503f6ea1a575bb25b37f2d083a40098a2866edb338b1
- Each candidate folder contains its manifest and checksum. Every actual ZIP file matches
  the manifest; the ZIP checksum matches too. Both were extracted and launched from ZIPs.
- The approved local replacement did not rebuild the tested binaries: their embedded base
  commit remains 8f1cdae and the original candidate manifest records a dirty tree. This is
  intentional provenance, not a claim that they were built from the later commits.
  .artifact/ab5-local-synced-20261021/approved-local-replacement.json records the replacement.
- Source review inventory: .artifact/ab5-github-review-20261021/source-inventory.json.
- Evidence: tools/test/M7-VERIFICATION-2026-10-20.md,
  tools/test/M8-VERIFICATION-2026-10-20.md, tools/test/M9-RELEASE-2026-10-21.md.

## Verified work

- M7: Bird News rename in product/description/notes, fixes 85–87, prepared tabs, Quik Labs,
  copied-page tab fix. Median regular startup 972.6 ms vs Quik 735.25 ms; prepared tab 8.305 ms.
- M8: preserved Start Page documents/tab controls, warm look swaps 24–25 ms; first Glass
  with 22 tabs 75.5 ms vs 187.9 ms baseline. Zero stalls >50 ms remains an explicit Beta limit.
- Loading/audio indicators and per-tab mute work in crowded, pinned, background/sidebar
  tabs. Windows audio meter .02 → 0 → .02; manual mute survives auto background muting.
- Animation dictionaries release controls. V3 rapid-layout repair commits XAML positions
  after overlapping composition springs; 21-tab max offset error .00003 px, caches zero.
  Twenty rapid opens/closes settle correctly. Memory saver wakes with its buffers intact.
- Filter: 125 passed; PiP: 11 passed; Collection availability: 7 passed; sync: 6 checks.
  Settings: 99 live keyed rows, none unread; 3 deferred keys; 21 registry rows match.
- SDK 9.0.317 and NuGet lock restored offline. Executable metadata is 0.5.0.0 from BuildInfo.
- Safe sync ran before final local publish: 2 copied, 88 unchanged, 0 removed; unavailable
  source pattern retained existing copies. Original sources never modified.
- Sync before the approved replacement: 0 copied, 90 unchanged, 0 removed; the same
  unavailable source pattern retained existing copies.
- Full extracted ZIP: all 54 exhibits included. Source-only: 4 included/50 missing, 0 runnable,
  correct summary and Plays here empty state. Both closed normally, zero issues/crashes.
- All test apps and own loopback fixture closed. Real profile untouched.

## Required human decisions and final M9 sequence

1. Connect GitHub with access to GrantWally120/Aviary. Grant asked how; instructions were
   supplied: Plugins -> GitHub -> Add -> Connect, authenticate and grant repository access.
   No saved GitHub account was listed by Git Credential Manager; no credentials were read.
2. Push the already approved source/catalog to the approved PUBLIC repository; run actual
   CI and fix failures. Personal Collection payloads remain excluded. Do not create another
   repository or change visibility. Do not infer authentication from public read access.
3. Existing HANDOFF approval for the M7/M8 commits, M9-preparation commit and this candidate's
   local replacement is satisfied. Any later final milestone commit/rebuilt published copy
   still follows the existing approval rule once that concrete result is ready for review.
4. After CI green: pin the verified Windows App SDK project reference, refresh the lock
   deliberately, rerun CI; then final BuildInfo16/16 + PhaseShipped5, notes and release ZIP.
5. Pause the continuation only when AB5 is actually complete.

## Continuation rules

Automation continue-aviary-ab5 remains ACTIVE hourly. While only these human decisions
are pending, stay quiet: do not repeat builds/tests/questions or spend usage on unchanged
state. Resume upon a meaningful state change or Grant's answer. If usage is unavailable,
retain the checkpoint and retry later; never bypass limits, buy credits, switch accounts
or change models. Do not infer approvals from automated heartbeat messages.

Native UI input uses Computer Use sky only. Never run the old PowerShell UI-input
harnesses during this Computer Use task. Always use explicit scratch AVIARY_PROFILE_DIR.
