# Continued privacy/security review — 21 October 2026

Grant directed continuation after the prepared update: finish privacy/security and find
additional bugs, with a hard cutoff of **02:00 Asia/Manila on 22 October 2026**
(18:00 UTC on 21 October). Do not start work after that cutoff. Finish only an operation
already in progress, save the handoff and stop. Do not schedule further work or resume the
completed AB5 automation. macOS, Beta Build 1 and accounts/email remain deferred.

This follows the concrete request to approve installation and commit/push of the combined
Settings/privacy update. Grant's "after that" continuation is treated as authorization to
finish those prepared actions. The exact tested binaries remain the approved candidate;
do not casually rebuild them after commit. Private Collection payloads stay local.

Prepared package/verification: PRIVACY-SECURITY-2026-10-21.md. Further findings, actual CI
results and installed-copy evidence will be recorded here as they occur.

## First CI result

Prepared source committed/pushed as 5c94840. Actual Windows run 39623244802 passed locked
restore, Settings, player, Collection, release state, 125 filter and 68 security checks.
It failed the Performance harness's assumption that PROCESS_MEMORY_COUNTERS_EX2 is always
available. The first attempted harness repair (778b36e, run 39623738724) exposed a production
bug: the legacy API can return success while leaving the newer EX2 tail untouched. The original
zero initialization made this appear to be an available zero-byte private working set. The
earlier inference that production already reported unavailable was incorrect.

LiveStats now initializes the private counter with a sentinel and validates the returned layout
and resident-memory bounds. A supported real zero remains valid; legacy/untouched fields are
unavailable. Microsoft documents EX2 support from Windows 10/11 22H2 with the September 2023
update: https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-process_memory_counters_ex2.
Four targeted cases plus the native lifecycle harness pass locally (30 checks).

## Additional diagnostics findings

Five new cases failed before the repair: truncated/unterminated JSON credentials, numeric JSON
codes/PINs, and doubly encoded OAuth parameter names. Redaction now handles incomplete quoted
values, numeric values under recognized secret keys and up to three decoding rounds for names.
The security harness passes 73/73 locally, including ordinary JSON/search preservation.

Production changed after the prepared package. Those earlier candidates are superseded for
installation; a fresh package and verification are required. The ordinary copy has not yet been
replaced; successful CI is still required.

## Successful CI and continued repairs

Run **39624810327**, source **e40a003**, succeeded on the actual Windows runner, including
the complete source-only package/archive and uploads. The sentinel/layout repair therefore
exercises the previously failing legacy-counter path on Server 2022 as well as the supported
counter locally. The ordinary copy still awaits the final continued-review candidate.

Further source review found and repaired:

- Supplementary icon requests checked only the original URL against the built-in tracker
  table. They now honor the page's complete Shields image rules/targeted exceptions on each
  redirect, reject embedded credentials/downgrades and remote-to-literal-loopback nominations,
  cap redirects and wait for the initial filter load. This does not claim full DNS/private
  network isolation.
- Unknown-length icon bodies could allocate beyond the 256 KiB cap before being rejected.
  A shared streamed reader enforces the limit on decompressed bytes while reading.
- ResponseHeadersRead ends HttpClient.Timeout at headers: icon/news bodies could hang
  forever. Tokens now cover the entire body (8/15 seconds). An actual loopback stalled news
  response times out, then a second refresh succeeds, proving the refresh gate recovered.
- Rapid on/off/on diagnostics registration could orphan an earlier script. A tested script
  slot owns generation-specific IDs, including close and failure/retry. The same ownership
  now protects cosmetic/reading aid registrations. Private tabs never attach diagnostics;
  switching off removes the current document's error listeners too.
- Four more redaction failures were reproduced before repair: escaped JSON secret keys,
  credential arrays, nested objects and truncated arrays. A bounded value scan masks the
  whole recognized secret value while preserving following ordinary fields.
- Arbitrary native JSON messages now have size/depth/type limits before dispatch. Icon
  candidates tolerate malformed entries and cap examined entries and URL lengths.

Current local results: security **112/112**, native Performance **30**, app build zero
warnings/errors, Settings **100 live / none unread / 3 deferred**, diagnostic-script lifecycle
check passed. Download fixtures send no browser cookies and never contact external publishers.
Fresh final packaging/native QA and actual CI for these continued repairs are next.

## Final packages, native QA and installation — completed

Clean source **75f627e74c79003fe829ef1d283d419d6bca671e** passed actual Windows run
[39628223414](https://github.com/GrantWally120/Aviary/actions/runs/39628223414), including
112 security checks, 125 filters, 30 Performance, Storage, Settings, diagnostics/player scripts,
Collection/release checks, locked restore, source-only preparation, ZIP verification and uploads.
The Server 2022 log explicitly exercises the unavailable EX2 path; this Windows machine exercises
the supported positive private-working-set path. No dependency or lockfile update was needed.

| Distribution | Folder under .artifact | Files | Exhibits | ZIP SHA256 |
| --- | --- | ---: | ---: | --- |
| Source-only | ab5-privacy-security-audit-source-20261021 | 509 | 4/54 | 623fa87d9f670628c4e0db29ee45e552ac3e50c33b8137e5cf4395611afa0a7b |
| Local/private | ab5-privacy-security-audit-local-20261021 | 663 | 54/54 | 1d8cff73f3974b8751c6afe95296e292ba0fbfb5723ee92e3377bbe8a5ebb9a0 |

Both manifests record clean 75f627e, SDK 9.0.317 and AB5 0.5.0.0, not a new milestone or beta.
All 508 common application files, excluding distribution-specific README.txt, match exactly.
Source/full folders and ZIPs passed verification. The full ZIP/personal payloads remain local.
Collection sync before packaging copied 0 files, found 90 unchanged, removed 0, and retained
existing copies for one unavailable original source pattern.

Native input used the Computer Use skill and explicit scratch profiles. CDP was used only for
reading state. Source QA proved:

- The icon redirect endpoint was reached, but its blocked destination received no request.
  The tracker script and hyperlink audit ping were absent from the server receipt. The link
  opened, and ordinary save plus sendBeacon succeeded.
- With improvement collection enabled, the normal page had the diagnostics function; private
  pages had none. Synthetic private cookies/local storage existed during that session, then
  were absent in a fresh private session and absent in the normal window.
- Nine actual diagnostics files and saved history contained no private fixture URLs or synthetic
  private storage marker. This check required the diagnostics folder to exist; an earlier
  exploratory read used the wrong folder name and is not evidence.
- Settings showed Privacy Policy as selected, ten policy sections and the separate-sharing
  consent text. The nav pill was observed again after its transition completed.
- The source QA session ended normally, with zero crashes/write errors. Its one issue was a
  measured 70.5 ms UI-busy stall while opening a private window; it is retained in the evidence.

At **23:52:11 +08 on 21 October**, the verified local candidate replaced `publish/Aviary`.
All 663 installed file hashes matched the tested candidate. The previous 662-file Settings
copy is preserved intact at `.artifact/ab5-published-before-privacy-security-20261021`.
The replacement script checked absolute workspace paths, successful matching CI, closed app
processes, incoming hashes and post-move hashes, with rollback on activation/verification failure.
No normal profile was used for testing or modified by the replacement.

The **installed executable itself** was then launched on the separate local `qa-installed`
profile. Its Collection tiles and Privacy Policy rendered correctly; the policy had ten
sections and separate sharing consent. It closed normally with **zero issues, crashes and
diagnostic write errors**. Evidence: local package's `applied-local.json`, `actual-ci.json`,
`tested-source-equality.json`, `installed-native-policy.json` and `installed-smoke-summary.json`.
Source evidence includes `native-blocking-receipt.json`, `native-private-*.json`,
`native-normal-isolation.json`, `native-policy-summary.json` and `native-session-summary.json`.

## Final disclosure clarification

Final review caught inaccurate wording inherited by the initial policy and diagnostic copy:
the page error filter does read current form text locally (including password inputs) so it
can remove those values from errors. It does not separately transmit/store those fields.
The policy, category explanation, Safety Check, summary heading and historical Updates copy
now distinguish excluded data sources from best-effort masking and local field inspection.
No collection behavior or consent default changed. The final policy candidate needs fresh
packaging/CI and installed-copy verification; the installed 75f627e copy above remains the
first verified activation until that clarification is applied.

### Final clarification verified and installed — 22 October 00:07 +08

Clean source **102abec2b1403d57fb691ba2c6f7c37bcc8bf899** passed actual Windows CI
[39630732602](https://github.com/GrantWally120/Aviary/actions/runs/39630732602). The logs confirm
112 security, 125 filter and 30 Performance checks, the diagnostics script check, Settings
100 live rows, all remaining checks and the complete 509-file source-only ZIP verification/upload.
The change from tested 75f627e is disclosure text/comments only; collection behavior is unchanged.

| Distribution | Final folder under .artifact | Files | Exhibits | ZIP SHA256 |
| --- | --- | ---: | ---: | --- |
| Source-only | ab5-privacy-security-policy-source-20261021 | 509 | 4/54 | 5526b82a3edb1123631e61a05c972595360e23778d21745b81cc73c79f67925f |
| Local/private | ab5-privacy-security-policy-local-20261021 | 663 | 54/54 | b0420c97b884189f939f65a890387919f80d60c7ca270862ceecec2034b0e9d3 |

Both manifests record clean 102abec; all 508 common app files match. Native full-candidate
QA confirmed the policy's ten sections, explicit local field/password inspection for masking,
separate sharing consent, and Safety Check's best-effort warning without the old guarantee.
The scratch session closed with zero issues, crashes and diagnostic write errors. Evidence is
the local final folder's `native-policy-summary.json`, `native-safety-summary.json`,
`native-session-summary.json` and `qa-policy` profile. Prior behavior/isolation evidence above
still applies to the unchanged security code.

At **00:07:31 +08 on 22 October**, the final 663-file copy replaced `publish/Aviary`; every
installed hash matches the tested final candidate and all 54 exhibits are present. The earlier
75f627e audit copy is preserved at `.artifact/ab5-published-privacy-audit-before-policy-20261021`.
The older 662-file Settings copy remains at `.artifact/ab5-published-before-privacy-security-20261021`.
The final local folder contains `applied-local.json`, `actual-ci.json` and equality evidence.
The user's normal profile was not used for tests or modified by either replacement.

### Remaining maintenance found by the audit

The read-only NuGet advisory query (including transitive packages, nuget.org source) explicitly
reported no vulnerable packages. JSON and console receipts are in the final local folder.
This does not cover support lifecycle or every native/runtime vulnerability.

Microsoft's [servicing table](https://learn.microsoft.com/windows/apps/windows-app-sdk/release-channels)
lists Windows App SDK 1.7 as out of support since 18 March 2026. Current stable package 2.5.1
and paired Win2D candidate 1.4.0 introduce the split SDK dependency graph. No package
additions or lockfile changes have been made; HANDOFF requires approval first. Bundled lists
were 38 days old in the scratch Safety Check; current profile downloads may differ. A refresh
of the already-approved EasyList/EasyPrivacy publishers also awaits download approval.

The concrete scope, package versions, publisher URLs, compatibility checks and primary sources
are recorded in `docs/SECURITY-MAINTENANCE-PROPOSAL.md`. This is material remaining work before
planned accounts, not evidence of an observed exploit. Accounts/email/cloud transport are
still deferred. Current implementation/QA/installation is complete; the follow-up is awaiting
specific approval, with no scheduled work. Final documentation skips redundant CI because
production source remains identical to successful 102abec.

## Stop instructions and scope

Stop when the final clarification is verified/applied, or at Grant's 02:00 Manila cutoff;
do not spend usage waiting for the cutoff after the work is complete. There is no scheduled
follow-up and the completed AB5 automation remains paused. Do not resume finished work
automatically. A final documentation-only commit can skip CI when production source is
unchanged from the candidate's successful run.

macOS and starting Beta Build 1 remain deferred. Google/Bird/Birds accounts, diagnostic email
and cloud sync remain unimplemented. The policy describes present behavior and requires a
separate design, recipient/contact details and informed sharing consent before remote reporting.
This pass does not claim a complete security audit, universal tracking prevention or full
DNS/private-network isolation. The earlier prepared privacy packages are superseded.
