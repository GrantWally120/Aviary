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

The pinned Windows App SDK 1.7 was out of support (servicing ended 18 March 2026). On 23 October 2026,
with Grant's go-ahead, the project moved to the supported 2.5.1 (as its four components) and Win2D 1.4.0, and the
bundled EasyList and EasyPrivacy snapshots were refreshed from easylist.to. See
[the migration report](SDK-2.5-MIGRATION-2026-10-23.md) for what was run. It was installed and pushed the same
day. Cookie and Social lists were not refreshed.

## Groundwork for accounts (23 October 2026)

Prepared before the account system, so it is built on checked parts. **None of it is wired to a
screen**: no account, token, sign-in or report exists in Aviary yet, and PRIVACY.md is unchanged.
317 checks in `tools/test/security` cover it (`AccountChecks.cs`).

- **Content-Security-Policy for Aviary's own pages** (`PageCsp.cs`). The Start Page and Settings
  are opened with `NavigateToString`, so the policy is a `<meta>` tag with a fresh nonce for each
  build: only the page's own script runs, with no inline handlers, no eval, no plugins, no frames,
  no forms, no `<base>`, no network connections, pictures only from `data:` (Settings) or from the
  three virtual hosts the Start Page draws from. The two inline `onerror` attributes in the Start
  Page became one delegated listener. `PageCsp.Apply` refuses a template that lost its marker, its
  nonce, or has a second script, and the tests fail on any inline `on…=` attribute, `javascript:`
  link or eval pattern in either template. Styles keep `'unsafe-inline'` (generated markup carries
  style attributes); that is not a route to script. The JSON written into the script block is
  HTML-escaped by the serializer, which a test also pins.
- **Token vault** (`TokenVault.cs`). One file per secret, sealed with DPAPI for the Windows user
  (`CryptProtectData`, no UI, entropy bound to the secret's name so a renamed copy does not open).
  Names are a closed alphabet; secrets are capped at 16 KiB and 64 entries; writes are atomic;
  tamper, truncation, foreign files and a failing protector all return `null`, never an exception
  carrying data; deletion overwrites first; nothing in the file logs. When accounts are wired:
  create it at `AppPaths.Of("account.vault")`, call `Wipe()` from "Forget everything" and from
  sign-out, and add the folder to Storage Usage's Secrets category. The diagnostics collector only
  reads its own folder, so the vault is already outside it.
- **Sign-in primitives** (`OAuthFlow.cs`). S256 PKCE (checked against the RFC 7636 vector), state
  and nonce from a CSPRNG, an authorization-URL builder that accepts only an https endpoint, a
  loopback redirect with a port and rejects anything that could smuggle a parameter, a callback
  parser (exact path, no repeated parameters, constant-time state comparison, bounded code, provider
  errors reduced to a plain word), and an ID-token claims check (issuer, audience list with `azp`,
  expiry and issued-at with skew, nonce, subject). It decodes but does **not** verify a token's
  signature: with the code flow the token arrives from the TLS token endpoint, which OpenID Connect
  Core 3.1.3.7 accepts; any token that arrives another way must be verified against the provider's
  keys first.
- **Loopback receiver** (`LoopbackReceiver`). A `TcpListener` on 127.0.0.1 and a system-chosen
  port (not reachable from the network, tested). It answers only GET to the exact callback path with
  the exact `Host: 127.0.0.1:<port>` (a DNS-rebinding name is refused), reads at most 8 KiB, turns
  away favicon and probe requests without ending the sign-in, and ends it after three requests that
  do not carry our state. The reply is a fixed sentence (nothing from the request is echoed) with
  `no-store`, `no-referrer` and its own policy, and it strips the code from the address bar.
- **Scrubber** (`Diagnostics.Scrub.cs`). Header lines are now masked by the same rule as parameters
  (any header whose name is a secret's: X-Auth-Token, X-Amz-Security-Token, …, not a fixed five);
  new secret names cover passphrases, bearer values, second-factor codes (`totp`, `hotp`, `mfa`),
  recovery/backup/security/device codes, client assertions, seeds, mnemonics, `cvv`/`cvc`, in
  query strings and JSON, spelled `snake_case` or `camelCase`. Ordinary text such as `keyword=`
  or `zipcode=` stays readable.
- **Reviewed, unchanged**: the WebView2 switches in `WebViewEnvironment.BuildArguments` and the
  profile settings (host objects off, reputation checking on, password and form autofill off in
  private tabs) already follow the rule that each flag is checked against the shipped engine; no
  new flag was added without being able to check it.

Still open before accounts: the operator, name, contact and backend decisions above; the Windows
App SDK servicing update and filter-list refresh (need approval, see the maintenance proposal);
a signed-in tab model for "who is signed in" that does not exist yet; revoking tokens at the
provider on sign-out; and a threat-model review of whatever the first account feature sends.

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
