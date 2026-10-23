# Prism 2 fixes and account groundwork

23 October 2026. Installed in `publish/Aviary`, committed and pushed on 23 October with Grant's approval. `artemis.html` was not touched.

Source: `Prism Review 2026-10-02/Prism-Review-2026-10-02.md` (findings P2-1 to P2-7) and
`Claude-Code-Prompt-Prism2-Specimen.md`. Request: fix Prism 2, prepare the Artemis adaptation, and add
security and privacy work before the accounts system.

## 1. Prism 2 problems

| # | Finding | Result | Evidence |
|---|---|---|---|
| P2-1 | Small grey text and the primary button fall short of AA | **Fixed.** Text tokens raised (light 2nd/3rd 80%/76%, dark 78%/72%), primary button `#0071E3` / `#0A6FDB`, status tags and node dots got their own ink and solid colours, hero italic word `--accent-ink` | Pixel-sampled contrast: 100% of characters at AA on both pages and the specimen, light and dark, desktop and 390 px. 20 colour checks in CI |
| P2-2 | The rim brightens the edge but does not refract | **Fixed.** The capsule is now a body layer and a sibling rim layer that never overlap, so the rim filters the page and not its own blurred parent | Edge transition at the rim: 3 to 3.5 px; before, 39 px (the same as no rim at all). Nested backdrop filters on the landing page: 27 to 0 |
| P2-3 | The landing page shows more than the Windows app can do | **Fixed.** Caption under the demo, and both "Liquid Glass" paragraphs say the Windows app keeps sheen, ring, edge lights, shadow and press but has no backdrop blur | `Aviary.html` |
| P2-4 | No contrast, transparency or forced-colours handling on the web pages | **Fixed.** Reduced transparency, increased contrast, forced colours, reduced motion and a no-backdrop fallback, written once in the tokens, as media queries and as `data-prism-mode` attributes | Smoke checks per mode: text kept, glass bodies drop their blur |
| P2-5 | The numbers live in three places | **Fixed.** One file, `design/prism2.tokens.json`; `node tools/design/prism2.js build` writes marked regions in `Aviary.html`, `Aviary-Phase0.html`, `LiquidGlass.xaml` and the specimen; `check` fails if a copy drifts. Google Fonts links removed (no request leaves the page) | `prism2-tokens-test.js` runs the drift check in CI |
| P2-6 | Native label colours look too faint | **Fixed and measured in the running app.** Alphas raised in the Windows labels. Measuring a real window showed the address-box hint (tertiary label on the field fill) at 4.23:1 in light, below AA, which the token test missed; it now models the field fill and the hint is 0.78 alpha | Calculated 4.24, measured 4.23 (so the model is the real one); re-measured after the fix, see section 4 |
| P2-7 | The 23 Sep tab-strip overlap bug is untested | **Tested, not reproducible.** `tools/test/tabstrip-overlap.ps1` drives the real app (CDP key events, UI Automation rectangles) | Passes in Liquid Glass on the installed copy |

`Classic.xaml` has the same faint labels. I did not change it: the review was about Prism 2.

## 2. Artemis adaptation

`E:\Claude CoWork Enclosed\Prism2-Specimen.html` is the single spec Artemis ports from: one offline file
(about 97 KB, no request), generated from the same tokens. It has the token table with live contrast
chips, a material lab (layer toggles, sliders, Copy CSS), the five rules with Do/Don't demos, the rim
lab (nested, none, sibling: 56 / 54 / 3 px), an Artemis mock in Prism 2 (Home, Chat, Settings,
History: floating header and composer capsules, scroll-edge scrims, read surfaces, amber send
button), the accessibility modes, the two tiers and a Chromium-only refraction experiment.
`node tools/test/prism/specimen.js` checks the definition of done: **92 of 92**, with screenshots in
`Prism Review 2026-10-02/specimen-shots/` (48 images).

Porting rules that came out of the work: a capsule's text must sit in elements, never bare inside
`.gl` (the body layer paints over it); no filter may have a filtered ancestor; read surfaces for
long text; take colours from the tokens, never copy numbers.

## 3. Security and privacy before accounts

None of it is wired to a screen; no account, token or sign-in exists. Details in `docs/SECURITY.md`.

- **Content-Security-Policy for the Start Page and Settings** (`PageCsp.cs`): a nonce per build, only
  the page's own script runs, no inline handlers, eval, frames, forms, plugins, `<base>` or network
  connections, pictures only from the hosts the page draws from. The two inline `onerror` attributes
  became one delegated listener. Checked in the real engine (`tools/test/csp-live.js`): the Start Page
  and all 22 Settings sections draw with zero violations or errors, and injected inline handlers,
  unnonced scripts, `javascript:` links, `eval`, `new Function`, string timers, form posts, frames,
  objects, outside pictures and outside fetches are each refused: **33 of 33**.
- **Token vault** (`TokenVault.cs`): DPAPI per Windows user, entropy bound to the secret's name, closed
  name alphabet, size and count limits, atomic writes, quiet failure on tamper, overwrite on delete.
- **Sign-in primitives** (`OAuthFlow.cs`): S256 PKCE (RFC 7636 vector), state and nonce, a strict
  authorization-URL builder, a callback parser with constant-time state comparison, an ID-token claims
  check, and a loopback receiver on 127.0.0.1 that checks Host, method, path, size and state, ignores
  probes, gives up after three forged callbacks, replies with a fixed page and strips the code from
  the address bar. Signature verification is deliberately left to the token-endpoint TLS exchange; see
  the file header.
- **Scrubber**: any header whose name is a secret's (not a fixed five); passphrases, bearer values,
  TOTP/MFA, recovery/backup/security/device codes, client assertions, seeds, mnemonics, CVV/CVC, in query
  strings and JSON, `snake_case` or `camelCase`; ordinary text still readable.
- **Reviewed, unchanged**: WebView2 switches and profile settings. They are checked against the shipped
  engine one at a time, and I could not check a new one, so I added none.

## 4. What was run

| Check | Result |
|---|---|
| Security harness (dotnet) | **317 passed, 0 failed** (was 128; 189 new: CSP, vault with real DPAPI, PKCE/callback/claims, loopback receiver on real sockets, scrubber) |
| Filters, performance, storage harnesses | 125, 30, 18 passed |
| settings-audit, m7-script, diagnostics-script, collection-availability, release-state | pass |
| Prism 2 token test (new, in CI) | 20 passed |
| Specimen definition of done | 92 of 92 |
| Page smoke (Aviary.html, Aviary-Phase0.html, specimen) | all passed |
| Live CSP in the built app | 33 of 33 |
| Native labels in the running app (Liquid Glass, PrintWindow, glyph extremes) | Address-box hint light 4.85:1 (was 4.23), dark 5.75:1; tab title 14.8:1 light, 8.9:1 dark. The faint "Aa" is the page-settings button, disabled on internal pages by design (`MainWindow.Reader.cs:117`) |
| Tab-strip overlap, Liquid Glass, built candidate | all checks passed |
| Publish smoke on the built candidate | 17 passed, 1 failed: "the Collection's files shipped with it" (7 files), because the candidate is `-SourceOnly`; a full build carries them |

Build: `dotnet restore` ran from the local NuGet cache only (`--source` the cache folder, locked mode),
so no package was downloaded or added. The candidate was built with `tools/release/prepare.ps1
-SourceOnly` under `.artifact/`.

## 5. Installed, and what still needs a decision

- **Installed** over `publish/Aviary` after checking the old install matched its rollback manifest (663 of 663).
  The new copy has 663 files, 54/54 exhibits, every hash matching `Backup/manifest-prism2.json`; four files differ from
  the previous install (Aviary.dll, Aviary.exe, Aviary.pdb, resources.pri). `publish-smoke.ps1 -Sha 5efad38`: 18/18.
  Live CSP on the installed copy: 33/33. The earlier source-only candidate (17/18) was only a test build; the one that
  went in includes the Collection.
- **Rollback:** `Backup/Aviary-AB5-fixes-local-win-x64.zip` (the previous install), and the same files unpacked in
  `.artifact/published-before-prism2-20261023`, which can be deleted once you are happy. `Backup/README.txt` lists all
  three packages.
- **Windows App SDK 2.5.1 / Win2D 1.4.0 and filter-list refresh** (`docs/SECURITY-MAINTENANCE-PROPOSAL.md`) need
  explicit approval: new NuGet packages and downloads. Not done.
- **Before the accounts system itself:** operator, name, private contact, backend, retention; revoking
  tokens at the provider on sign-out; Storage Usage's Secrets category and "Forget everything" to cover the
  vault folder (listed in SECURITY.md); a threat-model review of the first account feature.