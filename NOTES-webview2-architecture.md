# WebView2Browser — what the reference sample teaches us

Read before Phase 1. Source: [MicrosoftEdge/WebView2Browser](https://github.com/MicrosoftEdge/WebView2Browser)
(Win32/C++, last pushed July 2023, effectively frozen).

## What the sample actually does

It builds a tabbed browser out of **N + 2 WebView2 instances** in one HWND: a "controls"
WebView that renders the browser chrome as HTML, an "options" WebView for the dropdown
menu, and one content WebView per tab. It deliberately uses **two** `CoreWebView2Environment`s
with two user data folders (UI vs content), which means two independent WebView2 process
groups. The host app is a pure message broker — everything crosses as JSON
`{message: <int>, args: {...}}` over `PostWebMessageAsJson` / `window.chrome.webview.postMessage`,
against a hand-duplicated command table (`MG_NAVIGATE`, `MG_CREATE_TAB`, `MG_SWITCH_TAB`, …)
maintained in **both** `framework.h` and `wvbrowser_ui/commands.js`.

## Keep

- **The tab ownership shape.** One object per tab owning exactly one WebView2 plus its event
  registrations, held in a keyed collection with a separate `activeTabId`. For us:
  `BrowserTab` owning one `Microsoft.UI.Xaml.Controls.WebView2`, in an
  `ObservableCollection<BrowserTab>` with a `SelectedTab`.
- **One WebView2 per tab.** Expensive, but correct for a browser: per-tab back/forward
  history, per-tab process isolation and instant switching all come free. The alternative
  (one WebView re-navigated on switch) destroys history and makes switching feel like a load.
- **Show/hide, never create/destroy, on switch** — and show the incoming tab *before* hiding
  the outgoing one, which avoids a flash of empty window.
- **Death reconciliation.** When an operation on a tab fails because its CoreWebView2 is gone,
  tear the tab out of the UI rather than throw.
- **The per-tab event contract:** `SourceChanged` (address bar), `HistoryChanged`
  (back/forward enablement), `NavigationStarting` (spinner, show cancel), `NavigationCompleted`
  (`IsSuccess`, show reload). Always update that tab's own state; only touch visible chrome
  when the event's tab is the active one.
- **Origin-check privileged messages.** Internal pages must be identified by exact URI
  (`SetVirtualHostNameToFolderMapping` + check `CoreWebView2.Source`), never by a flag in the
  message — otherwise any site can clear the user's cookies.
- **Separate chrome storage from visited-site storage.** Right intent, wrong mechanism.

## Change

- **One shared `CoreWebView2Environment`**, created once at startup and passed to every tab
  via `EnsureCoreWebView2Async(env)`. Microsoft's guidance is explicit about reusing one
  environment in tabbed interfaces. Two environments = two browser processes for no benefit.
- **Replace the two-user-data-folder trick with multi-profile:** `CreateCoreWebView2ControllerOptions()`
  → `ProfileName` / `IsInPrivateModeEnabled` → `EnsureCoreWebView2Async(env, options)`.
  One user data folder, one browser process, separate cookie jars.
- **Delete the entire HTML chrome layer.** Our chrome is XAML. That removes two WebView2
  instances, one environment, one browser process, the manual `put_Bounds` layout, and the
  whole `MG_*` int-command protocol (which becomes ordinary C# calls and bindings).
- **Replace the CDP hacks with first-class APIs.** `Page.stopLoading` → `CoreWebView2.Stop()`.
  `Network.clearBrowser*` → `Profile.ClearBrowsingDataAsync(...)`.
- **Replace `ExecuteScript` scraping** for title/favicon with `DocumentTitleChanged` +
  `DocumentTitle` and `FaviconChanged` + `GetFaviconAsync(...)`. Both post-date the sample.
- **Handle `NewWindowRequested`** (the sample doesn't — `target=_blank` escapes the app) and
  `WindowCloseRequested`.
- **Layout is free.** The WinUI 3 `WebView2` is a `FrameworkElement`; drop all the DPI and
  `WM_SIZE` math.
- **Native storage for history/favourites** (SQLite or JSON in LocalFolder), not IndexedDB
  inside a chrome WebView — which also makes history queryable for address-bar autocomplete.
- **Unpackaged builds must set a custom user data folder.** The default sits next to the .exe;
  in Program Files that fails with "Access Denied". Use `LocalApplicationData\Aviary\WebView2`.

## Risks to plan for

- **Memory.** Per-tab WebViews are costly. A single shared environment collapses the
  browser/GPU/audio processes to one set, leaving only per-renderer cost. If we ever
  accidentally create an environment per tab, the app is unusable at ~15 tabs.
- **The initially-hidden WebView bug.** A WebView2 created while `Visibility=Collapsed`, then
  shown, can render but be non-interactive to the mouse (microsoft-ui-xaml #6256) — exactly
  our background-tab case. Mitigation: create a tab's WebView lazily on first activation.
  Re-test on every Windows App SDK bump.
- **TabView leaks.** `Microsoft.UI.Xaml.Controls.TabView` has long-standing reports of keeping
  removed items alive. Never rely on removal to dispose: call `WebView2.Close()`, unsubscribe
  handlers, and verify with `CoreWebView2.OpenTaskManagerWindow()` that the renderer dies.
- **Focus and keyboard.** Ctrl+L / Ctrl+T / Ctrl+W must reach our chrome from inside page
  content — needs `AreBrowserAcceleratorKeysEnabled = false` plus `AcceleratorKeyPressed`.
- **Suspension is best-effort.** Pick *either* `TrySuspendAsync`/`Resume` *or*
  `MemoryUsageTargetLevel`; the docs say not to mix them. Gate suspension on
  `IsDocumentPlayingAudio` or media in background tabs visibly breaks.
- **Evergreen runtime.** It updates under us — handle `NewBrowserVersionAvailable`,
  `RestartRequested`, `BrowserProcessExited`, and a missing runtime.

## The Safari user agent (hard requirement #2)

`CoreWebView2Settings.UserAgent` is **per-CoreWebView2**, so genuinely per-tab. Set it after
init and *before* the first navigation:

```csharp
webView.CoreWebView2.Settings.UserAgent =
  "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 " +
  "(KHTML, like Gecko) Version/18.0 Safari/605.1.15";
```

Do **not** use `AdditionalBrowserArguments = "--user-agent=..."` — that is process-wide,
can't vary per tab, and can't change after the environment is created.

Be clear-eyed about the limits: the engine is still Chromium, so `navigator.userAgentData`,
`Sec-CH-UA` headers, WebGL/font/codec fingerprints and Chromium-only feature detection all
contradict the string. Sites that branch on Safari will serve WebKit-specific CSS and
workarounds that Chromium may mis-render, and some sites fail *harder* with a mismatched UA
than with an honest one. Expect per-origin breakage in media (HLS/DRM), payment sheets and
push. **Recommendation:** make it a per-tab, user-visible toggle rather than a silent global
default. Also note: by default the UA override applies to the associated service worker, and
with a shared profile two tabs on the same origin will fight over it.
