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
