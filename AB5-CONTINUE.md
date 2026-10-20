# Aviary AB5 continuation checkpoint — 21 October 2026

## Status: Alpha Build 5 complete — 16/16, published and verified

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
