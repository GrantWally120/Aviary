# Privacy and security before accounts

21 October 2026. This is an AB5 follow-up; 0.5.0 and 16/16 remain unchanged.
Google sign-in, Bird/Birds, cloud sync and diagnostic email are not implemented.

## Boundaries in this change

- WebView2 host objects are disabled; SmartScreen remains explicitly enabled.
- Certificate errors are cancelled and excluded from HTTPS-Only's HTTP fallback.
- Private permission decisions are not saved by the native handler. Permission requests
  from insecure non-loopback origins are denied; secure origins retain WebView2's default
  behavior. This does not implement a new permission manager or an account system.
- Script-initiated external app links are cancelled; user-initiated links retain OS checks.
- Internal messages require the random page token and an engine-confirmed about:blank
  document. Icon offers use the current engine document, not a website's claimed origin;
  they are capped at 32 candidates and ignored in private tabs.
- Private Shields overrides never remove persistent site choices. Shields and HTTP
  exceptions are forgotten when the last private window closes.
- Link tracking pings are blocked by default while Shields is up. This covers requests
  classified as Ping with the hyperlink audit Ping-To header, including first-party pings.
  Ordinary fetch and sendBeacon requests still follow filter rules. The existing noPings
  experiment remains broader and can also stop beacons.
- Filter-list exceptions now apply to Aviary's built-in tracker fallback, with type/domain
  restrictions and important-rule priority preserved. This permits targeted compatibility
  exceptions while the rest of Shields remains active.
- List updates use approved HTTPS publishers, no browser cookies/default credentials,
  no redirects, streamed byte limits, strict UTF-8 and a deadline covering the body.
  Test URLs require both a scratch profile and loopback. A rejected update keeps the old
  list. If every enabled list fails, the result is a failure even if other lists are off.
- Diagnostics masks OAuth state/nonce, JSON credentials, HTTP credential headers and
  credentials nested in encoded redirect URLs. Both always-on crash.log writers use the
  same scrubber. Existing logs are not rewritten. Masking cannot guarantee all secrets
  or personal information are removed; the policy and collection UI now say so.
- Engine crash dumps are configured for local custom reporting. SmartScreen and other
  WebView2/Windows reporting are separate and are disclosed in the policy.
- Supplementary site-icon downloads apply Shields image rules and targeted exceptions at
  every redirect. Cookies/credentials are disabled, HTTPS downgrades and public-page to
  literal loopback targets are rejected, redirects are capped at three, and the decompressed
  body is capped at 256 KiB with an eight-second whole-request deadline. This is not a
  complete DNS/private-network isolation layer; ordinary website requests remain governed
  by WebView2. Bird News now carries its 15-second deadline through the response body and
  releases the refresh gate after a timeout.
- Diagnostics scripts never attach to private tabs. Registered diagnostic/cosmetic/reading
  aid scripts use generation ownership so an on/off/on race cannot leave orphan scripts.
  Disabling diagnostics removes its listeners from the current document as well as future
  documents. The secret scrubber handles truncated JSON, escaped property names, arrays,
  objects, numeric codes and repeatedly encoded parameter names; it remains best effort.
- Native JSON messages have size/depth/type limits before dispatch. Larger Settings editor
  payloads are accepted only on an engine-confirmed internal document; the random page token
  remains required before any command is handled.

The policy in Settings and the shipped PRIVACY.md use PrivacyPolicy.cs as their source.
They explain that the page error filter checks current form text locally, including password
inputs, to mask errors; those field values are not separately sent/stored as diagnostic fields.
This is distinct from excluding form submissions, cookie stores and clipboard contents as
diagnostic sources. Masking remains best effort.
The public copy must include the real operator and an appropriate private contact channel
before an account/reporting service is offered. The current copy identifies the project
maintainer and directs questions to the person supplying the app or the repository;
public issues must never receive diagnostic attachments or credentials.

The current NuGet audit reported no known vulnerable packages, but Microsoft's servicing
policy lists the pinned Windows App SDK 1.7 as out of support. Bundled filter snapshots also
need freshness maintenance. These remaining items are prepared, with exact proposed package
targets and publisher URLs, in [the maintenance proposal](SECURITY-MAINTENANCE-PROPOSAL.md).
NuGet/list downloads await the specific approvals required by HANDOFF. Do not treat this
pass as establishing a fully supported account-service foundation until that work is resolved.

## Required design for future accounts and reports

1. Decide the operator, Bird/Birds name, private support contact, hosted policy URL, backend,
   recipients, retention, deletion and incident response. Local-only consent cannot be
   reused as remote-reporting consent. No sender credentials belong in the source tree.
2. Use the external system browser for Google authorization, authorization code with
   S256 PKCE, a fresh state and OIDC nonce where applicable, and a narrowly bound loopback
   callback. Validate state, issuer, audience, expiry and nonce. Do not embed Google's
   authorization page in Aviary's controllable WebView2 or spoof the user agent to evade
   Google's embedded-browser restrictions. A desktop client cannot keep a client secret.
3. Request only identity scopes needed for sign-in. Signing in does not require Gmail
   permission. Sending reports through a controlled backend avoids asking every user for
   mailbox access. Select the transport after the recipient, threat model and retention
   are decided; do not email raw browsing archives or account tokens.
4. Store tokens using Windows-protected credential storage, separate from settings,
   diagnostics and synced data. Keep authorization headers, tokens and callbacks out of
   all logs and crash breadcrumbs. Support sign-out, token revocation and deletion.
5. Make sharing a separate opt-in, off by default. Show the destination and a preview of
   a minimal redacted report before the user sends it. Exclude private browsing, passwords,
   cookies, tokens and full histories by design, rather than relying only on a text scrubber.
6. Use authenticated TLS, server-side authorization, bounded payloads, rate limits and
   abuse controls. Add threat-model tests before enabling the feature. Revise the policy
   and verify the actual behavior before requesting new consent or deploying.

## Reference documents consulted

- Microsoft: https://learn.microsoft.com/microsoft-edge/webview2/concepts/security
- Microsoft: https://learn.microsoft.com/microsoft-edge/webview2/concepts/data-privacy
- Google: https://developers.google.com/identity/protocols/oauth2/native-app
- Google: https://developers.google.com/identity/protocols/oauth2/policies
- RFC 8252: https://www.rfc-editor.org/rfc/rfc8252

This is a technical design record, not a certification that every threat is prevented or
that a future hosted service meets all legal obligations.
