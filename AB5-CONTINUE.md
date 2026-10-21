# Aviary AB5 continuation checkpoint — 21 October 2026

> **ACTIVE CONTINUATION — Grant directed completion after the prepared update.**
> Finish its commit/push/installation, then review privacy/security for more bugs until
> 02:00 Manila, 22 October. No new work after that cutoff; finish only what is in progress
> and stop. No automation restart. See `tools/test/PRIVACY-SECURITY-AUDIT-2026-10-21.md`.
> The prepared state below is retained as the pre-publication checkpoint.

## Prepared privacy/security follow-up — awaiting approval

Grant requested stronger privacy/security/blocking and a Privacy Policy before future
Google/Bird/Birds accounts. Implementation and local QA are complete; see
`tools/test/PRIVACY-SECURITY-2026-10-21.md` and `docs/SECURITY.md`.
No accounts, email sender or remote diagnostics were added. AB5 stays 0.5.0, 16/16.

- Final local candidate: `.artifact/ab5-privacy-security-final-local-20261021/Aviary`,
  663 files, 54/54 exhibits. Source-only: `ab5-privacy-security-final-source-20261021`,
  509 files, 4/54 exhibits. All 508 shared app files match; full package stays local.
- Offline policy, default link-ping protection, private Shields isolation, bounded filter
  downloads, message/permission boundaries, SmartScreen, local engine crash reporting and
  stronger secret masking including crash.log. See the verification record for limits.
- Native QA caught that certificate cancellation ends as ConnectionAborted. Final candidate
  retains the certificate event and shows its error with no HTTP Continue. Native blocking/
  control and policy passed; all four scratch sessions closed, zero crashes. One deliberate
  attack anomaly is documented. No QA/helper process remains.
- Security 68, filters 125, Performance 26, Storage 18, player 11, availability 7,
  release-state 8; Settings 100 live/none unread. No new dependency or remote list downloads.
- Earlier `.artifact/ab5-privacy-security-review-20261021` is superseded; do not install it.
- Prior work backed up at `.artifact/privacy-security-before-20261021`. Changes remain
  uncommitted/unpushed; no remote CI, installation or upload. Ordinary Desktop/taskbar copy
  still uses the 20:27 Settings update below. New commit/push/replacement approval is required.
- Do not repeat unchanged QA. Completed automation remains paused; macOS/Beta Build 1
  deferred; account name/operator/transport still require a separate design.

## Status: Alpha Build 5 complete — 16/16, published and verified

### Follow-up requested 21 October: live Settings and AB5 dropdown — applied locally

- Grant requested an AB5 badge dropdown, then expanded the request to clearer Performance
  details, more accurate measurements, real-time Storage, Settings optimisation and bug fixes.
- Grant then requested automatic MB/KB display and explicitly asked to fix his ordinary copy.
  That authorizes this local replacement; do not ask for local application approval again.
- The current tested candidate is `.artifact/ab5-settings-mb-20261021/Aviary`:
  0.5.0.0, 662 files, 54/54 Collection exhibits. It supersedes the dropdown-only candidate and
  both `ab5-settings-live-20261021` and `ab5-settings-live-final-20261021` candidates.
- Performance now has stable expandable cards/tab/process details, real CPU intervals,
  per-Aviary physical-engine GPU aggregation, honest unavailable/partial readings, decimal
  B/KB/MB/GB/TB display, shared-process explanations, atomic Shields windows and correct peak wording.
  Byte measurements retain precision until display. Small values automatically use KB;
  1 KB = 1,000 bytes and 1 MB = 1,000,000 bytes, in Storage, Performance and the overlay.
  Sorting cannot add chart samples. Closed details are filled lazily. Watcher startup,
  multiwindow ownership, queued deliveries and native close cleanup are fixed.
- Storage samples each file once, reconciles site/category totals, uses Windows allocation,
  deduplicates hard-link allocation, exposes skips/estimates, and coalesces simultaneous scans.
  Live polling waits 2–10 s after a completed scan (based on its cost), only while visible.
  Open categories and keyboard focus survive updates. Programmatic navigation stops watchers.
- AB5 opens all 16 dated milestone descriptions with click/keyboard support. Settings notes
  describe this follow-up; release counters remain 16/16, PhaseShipped=5, preRelease=false.
- Verification: 26 performance + 18 storage checks pass; settings audit 99 live/none unread;
  release-state 8, player 11 and Collection availability 7 pass. Both new harnesses are added
  to Windows CI, but this follow-up has NOT run on GitHub yet (no commit/push approval yet).
- Real native QA observed a 4 MiB scratch file appear/disappear automatically, exact category
  reconciliation, preserved expanded details/focus, keyboard sorting without duplicate graph
  timestamps, and both pages stopping when left. Final sampler read 8/8 processes, actual
  0.999 s interval, 0.94 ms sampler cost. These are observations, not universal targets.
- All three scratch sessions closed normally, zero crashes/issues; no QA process remains. Earlier final
  profile: `ab5-settings-live-final-qa-20261021`; earlier: `ab5-settings-live-qa-20261021`.
  Decimal-unit QA: `ab5-settings-mb-qa-20261021`, port 9433, 11 unit boundary/conversion
  cases and 26 performance checks passed again after the precision change. Native Storage
  showed 66.5 KB for 66,547 bytes; Performance showed 345.7 MB for 345,673,728 bytes.
  All native input used sky on an idle desktop. Scratch tests did not use the normal profile.
- Full record: `tools/test/SETTINGS-LIVE-2026-10-21.md`. Final manifest, native model reads
  and screenshots are in the final candidate folder. Sync: 0 copied, 90 unchanged, 0 removed.
- Applied at 20:27 +08 on 21 October to `publish/Aviary`, after closing ordinary Aviary
  normally. All 662 installed hashes match the tested candidate. Previous copy preserved at
  `.artifact/ab5-published-before-settings-mb-20261021`; proof: candidate `applied-local.json`.
  Reopened the normal copy (PID 38968), verified its exact publish path and both shortcuts.
  Its native Storage displays live decimal MB/KB; Updates expands all 16 milestones;
  Performance shows new expandable details. Left ordinary Aviary open on Performance.
- Follow-up source remains uncommitted/unpushed. Grant has not approved new Settings
  commits/push; historical M9 approval does not cover these. Do not repeat tests/builds or
  local replacement on unchanged continuation. AB5 remains complete; continuation stays
  paused and macOS/Beta Build 1 remain deferred.
- Grant's normal Desktop and taskbar shortcuts now both target this workspace's
  `publish/Aviary/Aviary.exe`; their former C: targets were older builds. Shortcut backups and
  verified normal launch: `.artifact/aviary-shortcuts-20261021-096d4a92/`.

Grant authorized completing the remaining AB5 milestones and continuing after usage resets.
Preserve all M7/M8 work. macOS and starting Beta Build 1 remain deferred. Use this chat's model.

- M7 f427fe0, M8 42c2671, M9 preparation 77f54fb, exact SDK pin 3fd2a8b are committed
  and pushed to the approved PUBLIC https://github.com/GrantWally120/Aviary.git.
- Grant explicitly approved final M9 commits/push (release plus CI/publication record), the
  landing fixes and the tested local replacement. Final release source is f196ea3; this
  completion record is the approved follow-up commit. No approval remains outstanding.
- Grant approved those commits, the public source/catalog upload, and the earlier local
  replacement. Do not ask for those approvals again. Normal Git Credential Manager login
  succeeded as GrantWally120; no credentials were printed or handled by the assistant.
- Real Windows CI PASSED on 77f54fb: Actions run 39507147323. After that evidence, Windows
  App SDK was pinned exactly to [1.7.260224002], with all seven resolved package versions
  and content hashes unchanged. Its second real CI PASSED on 3fd2a8b: run 39508704024.
  Both runs passed locked restore, focused checks, package verification and both uploads.
- Final-source CI PASSED on f196ea3: https://github.com/GrantWally120/Aviary/actions/runs/39512372624.
  It passed the new release-state checks, final ZIP naming, package verification and uploads.
- BuildInfo has Version=0.5.0, MilestonesDone=16, PhaseShipped=5, PreRelease=false.
  publish/Aviary contains the exact tested 662-file final candidate, installed with Grant's
  approval at 07:06 on 21 October. Every staged file matched the manifest before activation;
  published-folder verification passed: 0.5.0.0 and all 54 exhibits present.
- The former 15/16 copy is preserved at .artifact/ab5-published-rc-before-final-20261021.
  Its predecessor remains at .artifact/ab5-published-before-approved-20261021. No rebuild
  changed the tested binaries' provenance. Personal Collection payloads remain LOCAL.

## Final packages — supersede every earlier RC/review folder

| Distribution | Folder under .artifact | ZIP | Files | Included exhibits |
| --- | --- | --- | ---: | ---: |
| Local/private Collection | ab5-final-local-20261021 | Aviary-AB5-local-win-x64.zip | 662 | 54/54 |
| Public/source-only | ab5-final-source-20261021 | Aviary-AB5-source-win-x64.zip | 508 | 4/54 |

- Local ZIP SHA256: cedde3f848f0bd2b63f1b7f159d477b4250e054c529a9c8018078006057a4a7d.
- Source ZIP SHA256: f480d3e3ca326a04af10e0d4a2c562aabb68bf1ad3bbbc13df9d3aebf29e20ea.
- Each folder has manifest.json and checksum. Every actual ZIP entry matches the manifest
  by length/hash. Both exact ZIPs were extracted and launched on fresh scratch profiles.
- Tested binaries use base 3fd2a8b with the final changes uncommitted: manifests correctly
  record workingTreeDirty=true. Do not rebuild casually after committing; that changes the
  tested binaries' embedded commit stamp. Copy the exact approved tested candidate.
- The full package contains personal projects and remains LOCAL. Only source/catalog and
  source-only CI packages are approved for public upload.

## Final verification

- Release-state regression: 8 passed; filters 125; player actions 11; availability 7;
  sync safety 6; settings 99 live keyed rows, none unread, 3 deferred, 21 registry matches.
- Required sync before final packaging: 0 copied, 90 unchanged, 0 removed. One unavailable
  source pattern retained existing copies. Original projects were never changed.
- Full native ZIP: Start Page, Settings/Updates, Collection verified. Real injected BUILD
  reports 0.5.0, Alpha Build 5, preRelease=false, 16/16 and PhaseShipped=5. Updates shows
  Latest, dated final M9 notes, and preserved M7/Bird News/M8 notes. All 54 exhibits included.
- Source-only native ZIP: valid featured landing page, Collection 4 included/50 missing,
  0 runnable, 4 landing pages. Plays here shows an empty state. Its EXE, DLL and resources.pri
  are byte-identical to the full candidate's tested application files.
- Both closed normally, no crashes. Local QA recorded ONE known UI-busy stall of 51.2 ms;
  source QA recorded zero issues. The existing >50 ms Beta limitation remains documented.
- Profiles: ab5-final-local-qa-20261021 and ab5-final-source-qa-20261021. No Aviary test
  process remains. No normal-profile writes or default-profile test launches.
- Browser QA: both landing looks show completed AB5 and 16/16; betas remain planned.
  Both download links point to the approved GitHub Actions page. A necessary small status
  selector repair outside PHASES avoids selecting planned BB2 after the last alpha is done.
  Download text/buttons were updated in both looks. These exceptions are included in the
  final review; the historical PHASES-only rule does not authorize a general page redesign.
- Temporary browser tab and single-file loopback fixture are closed.

## Completion evidence

- Current record: tools/test/M9-FINAL-2026-10-21.md. Earlier M7/M8 evidence and the historical
  M9-RELEASE preparation record remain preserved.
- Machine evidence/source inventory: .artifact/ab5-github-review-20261021/. Actual successful
  CI outcomes: first-ci-run.json and pinned-ci-run.json. Native read-only records,
  final-native-sessions.json, final-roadmap-browser.json and ab5-final-roadmap.jpg are there.
- final-ci-run.json records the actual successful final run; final-ci-artifacts.json records
  the Aviary-AB5-source-win-x64.zip and Aviary-build-evidence uploads.
- .artifact/ab5-final-local-20261021/approved-final-publication.json records approval,
  time, previous copy, successful final-source CI and the tested binaries' base/dirty metadata.
- Sync immediately before activation: 0 copied, 90 unchanged, 0 removed; unavailable source
  pattern retained its copies. No published Aviary process was running. Staged hashes were
  checked again and activation waited for the completed successful final-source CI result.
- Public source-only package: https://github.com/GrantWally120/Aviary/actions/runs/39512372624/artifacts/12306442360.
  No GitHub Release/tag or full local ZIP upload was requested.

## Continuation rules

AB5 is complete. Pause continue-aviary-ab5 if still active; do not resume builds, tests or
publication on an unchanged heartbeat. macOS and Beta Build 1 remain deferred until Grant
requests new work. Native input uses Computer Use sky only; old PowerShell UI-input harnesses
were not rerun. Future tests still require explicit scratch AVIARY_PROFILE_DIR.
