# Privacy/security and policy follow-up — 21 October 2026

> Historical preparation/QA record. These packages are superseded by clean source **75f627e**
> and the installed audit candidate. Commit/push/installation were subsequently authorized
> and completed. See `PRIVACY-SECURITY-AUDIT-2026-10-21.md` for actual CI, newer repairs and
> installation proof. The pending-approval/no-CI statements below describe the earlier checkpoint.

Grant requested stronger privacy/security/blocking and a Privacy Policy before planned
Google/Bird/Birds accounts. Implementation and local verification are complete; new commit,
push and installed-copy approval are pending. No account system, email sender or cloud
reporting was added. The name is undecided. AB5 remains 0.5.0, 16/16; macOS and Beta Build 1
remain deferred, and the completed automation remains paused.

## Final review packages — not installed or uploaded

| Distribution | Folder under .artifact | Files | Included exhibits | ZIP SHA256 |
| --- | --- | ---: | ---: | --- |
| Source-only | ab5-privacy-security-final-source-20261021 | 509 | 4/54 | fe3414e97317d0b7747867193fca24228f4ef6bef8d00c0ab5f82c44b0d0f843 |
| Local/private | ab5-privacy-security-final-local-20261021 | 663 | 54/54 | 0d39437baa5399532a300e1074e56399a30f6124f484ef4c14af95ff5c27e367 |

Both folders and ZIPs passed verification. All **508 shared app files**, other than the
distribution-specific README.txt, match byte for byte: EXE, DLL, PDB, resources.pri, pages,
filters and PRIVACY.md. Proof: local folder's tested-source-equality.json. The full ZIP
contains personal Collection payloads and must remain local. No GitHub upload, release,
tag, commit or push was made. No actual remote CI covers these or the prior Settings changes.

The earlier ab5-privacy-security-review-20261021 package is **superseded**: it predates the
final certificate-event fix. Do not install it. It retains early/control evidence. The
ordinary Desktop/taskbar copy remains the Settings update installed at 20:27 +08. No normal
profile write/debug port, cache deletion or installed-copy replacement occurred.

## Changes

- Offline Privacy Policy in Settings, linked from Privacy and security and About; shared
  text source with packaged PRIVACY.md. Covers retained profile data, optional detailed local
  diagnostics, deletion scope, masking limits, private browsing, providers, WebView2/SmartScreen
  and separately consented future reporting. Diagnostics remains off by default.
- Default protection against hyperlink audit pings, including first-party pings, while Shields
  is up. Ordinary fetch/save and sendBeacon requests still follow lists. The broader noPings
  experiment, Standard/Strict and per-site choices remain.
- Targeted filter exceptions now apply to the built-in tracker fallback, retaining type/domain
  restrictions and important-rule priority. No extra remote lists were downloaded.
- Private Shields changes/reset never remove saved site choices; private up/down/mode and
  HTTP exceptions expire with the last private window. Cosmetic/network overrides agree.
- Explicit host-object disablement and SmartScreen; insecure non-loopback permission requests
  denied, private permission decisions not saved by the handler, script-initiated app links
  cancelled. Secure websites retain the engine's default permission behavior.
- Invalid certificates cancelled. **The event's status is retained:** this runtime reports
  cancelled certificate completion as ConnectionAborted. Merely removing certificate enums
  from HTTP fallback was insufficient. Final native behavior shows the certificate error
  with no Continue to Site, and a new navigation clears that error state.
- Internal messages require both the random token and engine-confirmed opaque document.
  Icon offers use the actual current document, cap candidates at 32, ignore private tabs.
- Filter updates use approved HTTPS destinations, no cookies/default credentials/redirects,
  body-inclusive deadlines, streamed byte limits, strict UTF-8 and content checks. Test URLs
  need scratch profile plus loopback. All-enabled failure is reported; old lists survive.
- Redaction covers OAuth state/nonce, JSON credentials, HTTP credential headers and nested
  encoded URL credentials. Both always-on crash.log writers now use the scrubber. Existing
  logs are not rewritten. Custom filter collection remains as Grant previously requested.
  Collection UI now describes masking as best effort rather than guaranteed.
- Engine crash dumps use local custom reporting. Dumps can contain sensitive memory and are
  separate from improvement records and their cap. This does not disable SmartScreen, required
  runtime diagnostics or Windows reporting. See PRIVACY.md and docs/SECURITY.md.

No dependency/lockfile changes, no new milestone or roadmap changes. Account design requirements
are recorded in docs/SECURITY.md: external browser/PKCE, minimal scopes, protected token storage,
separate report opt-in/preview, named recipient, retention/deletion and secured transport.

## Local checks

- App Release builds: zero warnings/errors. Both final publishes and ZIP verification passed.
- New shipping-source security harness: **68 passed**. Covers credential/OAuth masking, private
  store non-writes/expiry, filter exceptions/priority, trusted documents, permission origins,
  destination checks, declared/chunked byte limits, content/UTF-8, cancellation, actual loopback
  update, redirect rejection, all-enabled failure, old-list preservation and policy equality.
- Existing harnesses: filters **125**, Performance **26**, Storage **18**, player **11**,
  Collection availability **7**, release-state **8** passed. Settings: **100 live keyed rows**,
  none unread, 3 deferred, 21 experimental rows/registry entries, no mismatch.
- Security check added to Windows workflow alongside preserved Performance/Storage steps.
  These are local results; no remote CI was triggered. Test fixture server held its own EXE
  during one attempted harness rebuild; it was stopped and the final harness then passed.
- Collection sync: **0 copied, 90 unchanged, 0 removed**, 1 unavailable source pattern retained.
- Prior work backed up to .artifact/privacy-security-before-20261021, including binary patch.
  All **12 unrelated preexisting files** remain hash-identical; the seven shared documentation/
  integration files were extended. The Settings diff was reviewed against that checkpoint.
  No reset, clean, checkout, staging or commit was used.

## Native verification

All input used Computer Use sky on isolated app windows; desktop guard was clear. DevTools
was only used for reads, never navigation, input, toggles or grants. Scratch values were seeded
before launch. No privacy controls/permission prompts were changed through UI automation.
No real login, account, mailbox or user credential was used.

Final source session qa-final (PID 3416, port 9441):

1. HTTP cert-failure.test mapped to loopback upgraded to HTTPS. The synthetic untrusted
   certificate raised certificate-rejected. Native panel said its certificate was invalid,
   with **no Continue to Site**; server received zero application requests. Earlier attempts
   were invalid transport fixtures: Windows key import and serial/preconnection handling
   needed correction. Final TLS fixture handles concurrent connections with bounded waits.
2. A subsequent local fixture navigation worked. The tracker script and first-party audit ping
   never reached the server; ordinary POST save, beacon and target link did. Native page said
   trackerRan=false. Separate qa-control, seeded with audit blocking off, sent the audit ping,
   proving the control/endpoint worked. No certificate warning was bypassed.
3. Settings opened normally. Policy displayed all ten sections, revision 21 October 2026,
   7,502 characters, without horizontal page overflow. Both protection values remained true.
   First review also reused a Settings tab for a hostile synthetic setting message: refused,
   Shields remained on.
4. All **four scratch sessions** ended Aviary closed, zero crashes. First recorded exactly one
   intentional untrusted-message anomaly from the attack fixture; others recorded zero issues.
   No QA Aviary or fixture process remains. HTTP/TLS helpers were stopped.

Evidence: final source folder's native-certificate-state.json, native-policy-state.json,
native-blocking-proof.json, qa-final diagnostics and manifests/checksums. Control/TLS receipts
remain in the superseded review folder. Permission-dialog UX and external-app handoffs were
not exercised with real services; this is focused verification, not universal ad blocking,
anonymity or certification that an account/email service is ready.

Implementation is ready for Grant's review. Wait for explicit commit/push/replacement approval.
Keep full local ZIP private; do not repeat unchanged checks or builds.
