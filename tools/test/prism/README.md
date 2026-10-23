# Prism 2 measuring kit

Browser measurements for the Prism 2 ("Liquid Glass") look, written for the 2 Oct 2026 Prism review. Everything drives a
headless Edge or Chrome over the DevTools Protocol on a scratch profile (port 9555) with Node 22+ (global `WebSocket`),
no npm packages. The browser-free half (generated copies match the token file, token colours clear AA) is
`node tools/test/prism2-tokens-test.js` and runs in CI; these scripts need a browser, so run them by hand before a release.

| Script | What it answers |
|---|---|
| `lib.js` | CDP client, PNG decoder, WCAG helpers. `require` it, no CLI. |
| `contrast.js <page>` | Pixel-sampled contrast: hides all text, screenshots, samples the real background under each text run. Options in the file header (`--scheme`, `--width`, `--scrolls`, `--media`, `--exclude`, `--under`, `--css`, `--json`). Target: at least 99% of characters at AA in light and dark, desktop and 390 px. |
| `inventory.js <page>` | Every `backdrop-filter` (incl. `::before`/`::after`) and whether an ancestor element also has one. Prism 2: zero nested. |
| `rim.js <page>` | Edge transition (10%-90%) in a capsule's outer 7 px band vs its interior, over a hard black/white split. Sibling-layer rim: about 3 px against 55+ px. |
| `smoke.js <page>` | No console errors, no request to another host, no loose text inside `.gl`/`.gcap`, no horizontal scroll at 390 px, text kept with each accessibility mode forced on and glass bodies dropping their blur. |
| `specimen.js [path]` | Full definition of done for `Prism2-Specimen.html` (92 checks, writes screenshots). `--stamp` writes the measured rim numbers into the page. |
| `keys.js <port> <target> <combo...>` | Trusted key events into one page target (used by `tools/test/tabstrip-overlap.ps1`). |

Regenerate the looks first when you change a value: edit `design/prism2.tokens.json`, run `node tools/design/prism2.js build`
(rewrites the `prism2:begin/end` regions in `Aviary.html`, `Aviary-Phase0.html`, `LiquidGlass.xaml` and the specimen), then
`node tools/design/prism2.js check` and `node tools/test/prism2-tokens-test.js`.

A capsule's text must sit in elements, never as a bare text node directly inside `.gl`: the body layer (`.gl::before`) paints
above loose text and blurs it. `smoke.js` and the token test both catch this.
