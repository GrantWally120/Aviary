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
available. The Windows Server 2022 runner does not provide it; Aviary already exposes that
reading as unavailable rather than inventing a value. The harness now verifies both supported
and unavailable outcomes, including dependent memory values. No production code was weakened.
The ordinary copy has not yet been replaced; successful CI is still required.
