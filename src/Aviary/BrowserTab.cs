using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace Aviary;

/// <summary>
/// One tab: one WebView2, its own history, its own title, its own event subscriptions.
///
/// The shape is lifted from Microsoft's WebView2Browser sample, which is the one part of
/// that sample genuinely worth copying — an object per tab owning exactly one WebView plus
/// its registrations, held in a keyed collection with a separate "active" pointer.
/// </summary>
internal sealed partial class BrowserTab
{
    /// <summary>
    /// Safari 18 on macOS. Per-tab on purpose: CoreWebView2Settings.UserAgent is a
    /// per-WebView property, so a later phase can make this a switch on individual tabs
    /// rather than a silent global.
    /// </summary>
    internal const string SafariUserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 " +
        "(KHTML, like Gecko) Version/18.0 Safari/605.1.15";

    public Microsoft.UI.Xaml.Controls.WebView2 View { get; }

    /// <summary>A number for this tab that diagnostics records beside everything it logs.</summary>
    public int Id { get; } = Diagnostics.NewTabId();

    public string Title { get; private set; } = "New Tab";
    public string Url { get; private set; } = string.Empty;
    public bool IsSecure { get; private set; }
    public bool IsReady { get; private set; }

    /// <summary>
    /// The fallback letter, shown only when the site has no icon of its own. The colour
    /// behind it is no longer chosen here — the hues live in the theme dictionaries and
    /// FaviconView derives a stable index from the domain, so a site keeps its colour
    /// between launches instead of being assigned one in tab-open order.
    /// </summary>
    public string Monogram { get; private set; } = "•";

    /// <summary>This tab's favicon cache key, kept in step with <see cref="Url"/>.</summary>
    public string IconKey { get; private set; } = string.Empty;

    /// <summary>True for Aviary's own pages, which show the dome mark rather than a letter.</summary>
    public bool IsInternalPage => _internalTitle is not null;

    /// <summary>
    /// True between NavigationStarting and NavigationCompleted. Safari's reload glyph
    /// becomes a stop glyph while this is true — a browser that cannot be told to stop is
    /// one you have to wait for.
    /// </summary>
    public bool IsLoading { get; private set; }

    /// <summary>Raised whenever anything the chrome displays has changed.</summary>
    public event Action<BrowserTab>? Changed;

    /// <summary>Raised when the page asks to close itself (window.close()).</summary>
    public event Action<BrowserTab>? CloseRequested;

    /// <summary>
    /// Raised for a browser shortcut pressed while the PAGE had keyboard focus.
    ///
    /// XAML KeyboardAccelerators never see those: WebView2 hosts a child window that takes
    /// keyboard input before XAML's pipeline runs, and disabling Chromium's own
    /// accelerators stops it acting on them without handing them back. So the keys get
    /// caught inside the page and posted up.
    /// </summary>
    public event Action<string>? ShortcutPressed;

    /// <summary>Raised when a page starts downloading a file.</summary>
    public event Action<CoreWebView2DownloadOperation>? DownloadStarted;

    /// <summary>Raised when a navigation fails, so the chrome can show its own message.</summary>
    public event Action<BrowserTab, CoreWebView2WebErrorStatus>? NavigationFailed;

    /// <summary>Raised after a navigation SUCCEEDS, once the document is there to inspect.</summary>
    public event Action<BrowserTab>? Navigated;

    /// <summary>
    /// Raised when an HTTPS-Only upgrade failed: the site did not answer securely. Carries the
    /// original http:// address, which the fallback panel offers to continue to.
    /// </summary>
    public event Action<BrowserTab, string>? HttpsFallback;

    /// <summary>Raised when a navigation of this tab's own really starts (after Aviary's rewrites and Shields).</summary>
    public event Action<BrowserTab>? NavigationStarted;

    /// <summary>Raised when the renderer showing this tab's page has died; carries the engine's reason.</summary>
    public event Action<BrowserTab, string>? PageGone;

    /// <summary>What Aviary shows in place of this tab's page, if anything (MainWindow.Notices.cs, bug 65).</summary>
    internal TabNotice? Notice { get; set; }

    /// <summary>Raised when the page enters or leaves element full screen (a video's button).</summary>
    public event Action<BrowserTab, bool>? FullScreenChanged;

    /// <summary>
    /// A per-launch secret baked into the injected shortcut script and required on every
    /// shortcut message.
    ///
    /// Bug 38's follow-up: the Settings and Start Page messages were locked down in M1, but a
    /// {type:'shortcut'} message was still accepted from ANY page, so a website could close
    /// your tab, open tabs, or flip the sidebar just by posting one. The page cannot learn
    /// the token — it lives only in the injected script's closure, which runs before any page
    /// script and captures postMessage and JSON.stringify before a page can replace them —
    /// and synthetic key events are refused by isTrusted. Separate from the internal pages'
    /// PageToken on purpose: this one is present in every website's process, so it must not
    /// also be the key to Settings.
    /// </summary>
    internal static readonly string ShortcutToken =
        Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private void OnContainsFullScreenElementChanged(CoreWebView2 sender, object args)
    {
        try
        {
            FullScreenChanged?.Invoke(this, sender.ContainsFullScreenElement);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: full-screen element changed");
        }
    }

    /// <summary>Set for Aviary's own pages, whose titles we own rather than read.</summary>
    private string? _internalTitle;

    /// <summary>Kept so blocked requests can be answered with a synthetic response.</summary>
    private CoreWebView2Environment? _environment;

    /// <summary>
    /// When this tab last started a download (a Stopwatch timestamp).
    ///
    /// A navigation that turns into a download reports itself as ABORTED, because from the
    /// engine's point of view the page load really was cancelled — so without this every
    /// successful download would be followed by an error panel saying the connection was cut
    /// short. It used to be a flag set in DownloadStarting and read in NavigationCompleted,
    /// which only worked while DownloadStarting arrived FIRST. Runtime 152 raises
    /// NavigationCompleted 7 ms before it (bug 39, found in M2's download check): the panel
    /// covered the page after every download, and the flag, never consumed, then swallowed
    /// the next genuine failure. A timestamp compared against the navigation's own start,
    /// with a short grace period for the late event, works in either order.
    /// </summary>
    private long _downloadStartedAt;

    /// <summary>How long an aborted navigation waits to see whether it became a download.</summary>
    private const int DownloadGraceMs = 400;

    private bool DownloadStartedSince(long navStart) => navStart != 0 && _downloadStartedAt >= navStart;

    // ── diagnostics: the navigation in flight, and requests awaiting their response ──

    private ulong _navId;
    private long _navStart;
    private bool _navUserInitiated;
    private string _navKind = string.Empty;
    private readonly List<string> _navChain = new();
    private int _pageErrorsThisPage;
    private string _lastTitleLogged = string.Empty;
    private long _lastTitleAt;
    private string? _diagScriptId;
    private bool _diagnosticsWired;
    private static int _firstPage;

    /// <summary>
    /// When each request left, keyed "METHOD url", so its response can be timed. UI thread
    /// only (both events arrive there). Bounded: a page that fires requests which never answer
    /// must not grow it forever.
    /// </summary>
    private readonly Dictionary<string, (long At, string Context, string? BlockedBy)> _inFlight = new(StringComparer.Ordinal);

    /// <summary>
    /// A tab in a private window. Its engine runs on an InPrivate (off-the-record) profile —
    /// cookies, cache and site storage held in memory and discarded when the last private
    /// tab closes — and nothing about it is written anywhere by Aviary: not history, not the
    /// favicon cache, not the Privacy Report, not diagnostics.
    /// </summary>
    public bool IsPrivate { get; }

    /// <summary>Diagnostics' browsing categories, for THIS tab: on, and never for a private one.</summary>
    private bool Records => Diagnostics.RecordsBrowsing && !IsPrivate;

    public BrowserTab(bool isPrivate = false)
    {
        IsPrivate = isPrivate;
        if (isPrivate)
        {
            // So a crash report lists it as a private tab, never with its address or title.
            Diagnostics.TabIsPrivate(Id);
        }

        View = new Microsoft.UI.Xaml.Controls.WebView2
        {
            Visibility = Visibility.Collapsed,

            // Invisible until its first page has painted (Unveil, BrowserTab.Background.cs).
            Opacity = 0
        };
    }

    public async Task InitialiseAsync()
    {
        if (IsReady)
        {
            return;
        }

        CoreWebView2Environment environment = await WebViewEnvironment.GetAsync();
        _environment = environment;

        if (IsPrivate)
        {
            // The same shared environment (one browser process for everything), but an
            // off-the-record profile: WebView2 derives it from the regular profile and keeps
            // its network state in memory. Regular and InPrivate WebViews may run side by side.
            CoreWebView2ControllerOptions options = environment.CreateCoreWebView2ControllerOptions();
            options.IsInPrivateModeEnabled = true;
            await View.EnsureCoreWebView2Async(environment, options);
        }
        else
        {
            await View.EnsureCoreWebView2Async(environment);
        }

        if (View.CoreWebView2 is not CoreWebView2 core)
        {
            throw new InvalidOperationException("WebView2 initialised without a core.");
        }

        // The colour under the page (BrowserTab.Background.cs) was set before the engine existed;
        // hand it over again now that there is one, before anything navigates. And never leave a
        // tab invisible for long whatever happens to its first page.
        ApplyCanvasColour(force: true);
        _ = UnveilAfterAsync(UnveilDeadlineMs);

        if (Diagnostics.EngineVersion.Length == 0)
        {
            try { Diagnostics.EngineVersion = environment.BrowserVersionString; }
            catch (Exception ex) { Diagnostics.Swallowed(ex, "tab: read engine version"); }
        }

        // The Collection (https://collection.aviary/) is answered from Aviary's own folder by this
        // tab's engine. Per tab, before anything navigates — private tabs included.
        Library.Attach(core);

        // Before the first navigation. Setting it later only affects subsequent ones.
        _defaultUserAgent = core.Settings.UserAgent;
        await ApplyUserAgentAsync();
        await ApplyReferrerScriptAsync();
        core.Settings.IsStatusBarEnabled = false;

        core.SourceChanged += OnSourceChanged;
        core.HistoryChanged += OnHistoryChanged;
        core.DocumentTitleChanged += OnDocumentTitleChanged;
        core.FaviconChanged += OnFaviconChanged;
        core.NavigationStarting += OnNavigationStarting;
        core.ContentLoading += OnContentLoading;
        core.NavigationCompleted += OnNavigationCompleted;
        core.NewWindowRequested += OnNewWindowRequested;
        core.WindowCloseRequested += OnWindowCloseRequested;
        core.WebMessageReceived += OnWebMessageReceived;

        // Chromium would otherwise act on Ctrl+T itself and swallow it.
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;

        // Injected into EVERY document, including ones we did not write, so Ctrl+T keeps
        // working while the caret is inside a text box on somebody else's website. Carries the
        // per-launch shortcut token (see ShortcutToken).
        // Bug 91: FIRST, before any of the page's own script can add speculation rules.
        await core.AddScriptToExecuteOnDocumentCreatedAsync(NoSpeculationScript);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(
            ShortcutScript.Replace("__AVIARY_KEYTOKEN__", ShortcutToken));
        await core.AddScriptToExecuteOnDocumentCreatedAsync(FindInPage.Script);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(IconHarvestScript);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(Reader.Script);

        core.DownloadStarting += OnDownloadStarting;
        core.IsDefaultDownloadDialogOpenChanged += OnDefaultDownloadDialog;
        ApplyDownloadFolder();

        ApplyColorScheme();
        ApplyAutofill();
        ApplyZoom();

        // Every request passes through us before it leaves the machine — from EVERY source.
        //
        // The two-argument overload used until M2 is deprecated and, in Microsoft's own words,
        // "fires only for the main frame and its same-origin iframes". An ad is almost always
        // a cross-origin iframe, so everything an ad frame loaded — its scripts, its pixels,
        // the next tracker in the chain — went straight past the blocker unseen. All source
        // kinds also covers dedicated, shared and service workers.
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All,
            CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += OnWebResourceRequested;

        // The ad blocker's element hiding, and its reaction to list and Shields changes (M3).
        AttachShields();

        // A page asking for full screen (a video's full-screen button) takes the WINDOW full
        // screen, as every browser does; otherwise the video only fills the area under the
        // toolbar.
        core.ContainsFullScreenElementChanged += OnContainsFullScreenElementChanged;

        // Always on, whatever the diagnostics switch says: it is rare, it costs nothing until
        // it fires, and a renderer that dies should at least be known about.
        core.ProcessFailed += OnProcessFailed;

        // M6: which process is the engine, so "Restart Aviary" can wait for it to go.
        WebViewEnvironment.NoteBrowserProcess(core);

        Diagnostics.EnabledChanged += OnDiagnosticsChanged;
        if (Records)
        {
            await SetDiagnosticsAsync(true);
        }

        IsReady = true;
    }

    private void OnDiagnosticsChanged(bool on) => _ = SetDiagnosticsAsync(on);

    /// <summary>
    /// Wires or unwires the parts of diagnostics that cost something per page: the response
    /// listener (every response crosses into .NET while it is subscribed) and the page-error
    /// script. Nothing here is attached while collection is off.
    /// </summary>
    private async System.Threading.Tasks.Task SetDiagnosticsAsync(bool on)
    {
        try
        {
            if (View.CoreWebView2 is not CoreWebView2 core || on == _diagnosticsWired)
            {
                return;
            }

            _diagnosticsWired = on;

            if (on)
            {
                core.WebResourceResponseReceived += OnWebResourceResponseReceived;
                string id = await core.AddScriptToExecuteOnDocumentCreatedAsync(PageErrorScript);

                // Switched off again while that was in flight: take it straight back out.
                if (!_diagnosticsWired)
                {
                    core.RemoveScriptToExecuteOnDocumentCreated(id);
                    return;
                }

                _diagScriptId = id;

                // The document already showing has not run it; arm it too, so switching on
                // does not wait for the next navigation.
                if (_internalTitle is null)
                {
                    _ = core.ExecuteScriptAsync(PageErrorScript);
                }
            }
            else
            {
                core.WebResourceResponseReceived -= OnWebResourceResponseReceived;
                if (_diagScriptId is not null)
                {
                    core.RemoveScriptToExecuteOnDocumentCreated(_diagScriptId);
                    _diagScriptId = null;
                }

                _inFlight.Clear();
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: wire diagnostics");
        }
    }

    /// <summary>
    /// Ad and tracker requests are answered with a 403 of our own rather than forwarded.
    /// Nothing reaches the network, which is the whole point — a blocker that merely hides
    /// elements has already told the tracker you were here.
    /// </summary>
    private void OnWebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        try
        {
            // Aviary's own pages ask for site icons over a synthetic origin. TryServe
            // answers EVERY request to that host, a bad one included — if it fell through,
            // Chromium would do a real DNS lookup for aviary.localhost.
            string uri = args.Request.Uri;

            // M4.9: the Start Page's own background picture shares that host, and is asked about
            // first - the icons' server answers everything else that reaches it, a bad path included.
            if (_environment is not null
                && StartBackground.TryServe(uri, _environment, out CoreWebView2WebResourceResponse? picture))
            {
                args.Response = picture;
                return;
            }

            if (_environment is not null
                && Favicons.TryServe(uri, _environment, out CoreWebView2WebResourceResponse? icon))
            {
                args.Response = icon;
                return;
            }

            // The first moments after launch, while the filter lists are still being read (well
            // under a second): hold a website's request until they are, rather than wave its
            // first ads through. Never longer than two seconds.
            if (FilterLists.Engine is null && !FilterLists.Loading.IsCompleted && Shields.Enabled && _internalTitle is null)
            {
                Deferral deferral = args.GetDeferral();
                _ = DecideWhenListsLoadAsync(args, uri, deferral);
                return;
            }

            Decide(args, uri);
        }
        catch (Exception ex)
        {
            // Let the request through rather than take the page down. A missed advert is
            // cheaper than a broken browser.
            Diagnostics.Swallowed(ex, "tab: request filter");
        }
    }

    private async Task DecideWhenListsLoadAsync(CoreWebView2WebResourceRequestedEventArgs args, string uri, Deferral deferral)
    {
        try
        {
            await Task.WhenAny(FilterLists.Loading, Task.Delay(2000));
            Decide(args, uri);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: request filter, after waiting for the lists");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void Decide(CoreWebView2WebResourceRequestedEventArgs args, string uri)
    {
        // Timed for diagnostics' blocker-cost figure. GetTimestamp is a few nanoseconds;
        // FilterCost returns at once while collection is off.
        long matchStart = System.Diagnostics.Stopwatch.GetTimestamp();
        (string Rule, string List)? verdict = ShieldsVerdict(uri, args.ResourceContext);
        long matchTicks = System.Diagnostics.Stopwatch.GetTimestamp() - matchStart;
        Diagnostics.FilterCost(matchTicks);
        LiveStats.NoteBlocker(matchTicks);

        if (verdict is not { } stop || _environment is null)
        {
            // The privacy work on requests that DO go out (BrowserTab.Privacy.cs).
            CleanOutgoingRequest(args);
            TrimReferrer(args.Request, uri);

            if (Records)
            {
                NoteRequestLeft(args.Request.Method, uri, args.ResourceContext, null);
            }

            return;
        }

        TrackerBlocker.CountBlocked();
        CountBlockedOnPage(uri, stop.Rule, stop.List);
        QueueCollapse(uri, args.ResourceContext);

        // The session counter resets when you quit, so on its own it can never say
        // anything more interesting than "since you opened the app". The seven-day
        // store is what the Start Page's Privacy Report reads.
        // Never for a private tab: the report keeps which sites contacted trackers.
        if (!IsPrivate && Uri.TryCreate(uri, UriKind.Absolute, out Uri? blocked))
        {
            PrivacyReport.Record(blocked.Host, HostOf(Url), args.ResourceContext);
        }

        // A blocked FRAME gets 204 No Content, which Chromium never commits: the frame stays the
        // empty document it started as, inside the page's own process. A 403 was an error page
        // that Chromium loaded — and for a cross-site ad frame that meant a whole renderer
        // process of its own, ~4 MB apiece (seen in M1's minute samples; fixed in M3).
        args.Response = args.ResourceContext == CoreWebView2WebResourceContext.Document
            ? _environment.CreateWebResourceResponse(null, 204, "Blocked by Aviary", string.Empty)
            : _environment.CreateWebResourceResponse(null, 403, "Blocked by Aviary", string.Empty);

        if (Records)
        {
            Diagnostics.Blocked(Id, uri, stop.Rule, args.ResourceContext.ToString(), Url, stop.List);

            // Its synthetic 403 still arrives as a response; the network line should say
            // who answered it rather than look like the server refused.
            NoteRequestLeft(args.Request.Method, uri, args.ResourceContext, stop.Rule);
        }
    }

    private void NoteRequestLeft(string method, string uri, CoreWebView2WebResourceContext context, string? blockedBy)
    {
        if (_inFlight.Count >= 4000)
        {
            // Requests that never answered (a long-poll, a page torn down mid-load). Forget
            // them rather than let the map grow without bound.
            _inFlight.Clear();
        }

        string key = method + " " + uri;
        if (!_inFlight.ContainsKey(key))
        {
            _inFlight[key] = (System.Diagnostics.Stopwatch.GetTimestamp(), context.ToString(), blockedBy);
        }
    }

    /// <summary>
    /// Diagnostics category 3: every response, as it arrives. Only subscribed while
    /// collecting. Reads the status and two headers — type and length — and nothing else:
    /// never Set-Cookie, never the request's own headers.
    /// </summary>
    private void OnWebResourceResponseReceived(CoreWebView2 sender, CoreWebView2WebResourceResponseReceivedEventArgs args)
    {
        if (!Records)
        {
            return;
        }

        try
        {
            CoreWebView2WebResourceRequest request = args.Request;
            CoreWebView2WebResourceResponseView response = args.Response;

            string uri = request.Uri;

            // Aviary's own pages and the icons it serves them from memory are not network
            // traffic: nothing left the machine. Logging them made the Start Page look like a
            // 25,000-character request on every new tab.
            if (_internalTitle is not null
                || uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                || uri.StartsWith("https://aviary.localhost/", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string method = request.Method;
            string key = method + " " + uri;

            double? ms = null;
            string context = string.Empty;
            string? blockedBy = null;
            if (_inFlight.Remove(key, out (long At, string Context, string? BlockedBy) left))
            {
                ms = Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(left.At).TotalMilliseconds, 1);
                context = left.Context;
                blockedBy = left.BlockedBy;
            }

            string? type = null;
            long? bytes = null;
            CoreWebView2HttpResponseHeaders headers = response.Headers;

            if (headers.Contains("Content-Type"))
            {
                string raw = headers.GetHeader("Content-Type");
                int semicolon = raw.IndexOf(';');
                type = (semicolon >= 0 ? raw.Substring(0, semicolon) : raw).Trim();
            }

            if (headers.Contains("Content-Length")
                && long.TryParse(headers.GetHeader("Content-Length"), out long length))
            {
                bytes = length;
            }

            Diagnostics.Request(new NetRecord
            {
                Tab = Id,
                Url = uri,
                Method = method,
                Context = context,
                Status = response.StatusCode,
                BlockedBy = blockedBy,
                Type = type,
                Bytes = bytes,
                Ms = ms,
                PageUrl = Url,
            });
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: record response");
        }
    }

    /// <summary>
    /// Diagnostics category 9. A renderer, the GPU process or the engine itself died or hung.
    /// Each property is read on its own: older runtimes lack some, and one missing field must
    /// not lose the record.
    /// </summary>
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    /// <summary>SM_SHUTTINGDOWN: non-zero once Windows has begun to log off, restart or shut down (bug 83).</summary>
    private static bool WindowsIsShuttingDown()
    {
        // Test only: a shutdown cannot be staged on the machine running the test. Environment only,
        // like AVIARY_CRASH_TEST - nothing inside the browser can set it.
        if (Environment.GetEnvironmentVariable("AVIARY_SHUTDOWN_TEST") == "1")
        {
            return true;
        }

        try
        {
            return GetSystemMetrics(0x2000) != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args)
    {
        // Whatever the engine shows for a dead page must be seen, even in a tab still waiting for its first paint.
        Unveil();

        string Read(Func<object?> get)
        {
            try { return get()?.ToString() ?? string.Empty; }
            catch (Exception ex) { Diagnostics.Swallowed(ex, "tab: read engine-failure field"); return string.Empty; }
        }

        List<string> frames = new();
        try
        {
            if (args.FrameInfosForFailedProcess is { } infos)
            {
                foreach (CoreWebView2FrameInfo frame in infos)
                {
                    frames.Add(frame.Source);
                    if (frames.Count >= 10) { break; }
                }
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: read failed frames");
        }

        // A private tab's engine failure is still worth knowing about — without where it was.
        if (IsPrivate)
        {
            frames.Clear();
        }

        string kind = Read(() => args.ProcessFailedKind);
        string reason = Read(() => args.Reason);

        // M4.9 (bug 83, from Grant's own diagnostics, 21 Sep 16:18): Windows restarting closes the
        // engine's processes a moment BEFORE it tells Aviary the session is ending, so every open
        // tab reported "BrowserProcessExited — Unexpected" and five engine failures sat under Needs
        // attention for a PC that had simply been restarted. SM_SHUTTINGDOWN is set from the moment
        // a shutdown begins; while it is, an engine exit is Windows closing things, and is recorded
        // as that - once, as a session event - rather than as a failure per tab.
        if (WindowsIsShuttingDown())
        {
            Diagnostics.EngineClosedByShutdown(kind);
            return;
        }

        // The renderer of THIS page (not a frame's, not the GPU process — those recover by
        // themselves) is gone: Aviary's panel, with Reload, instead of Chromium's (HANDOFF §8).
        if (kind == nameof(CoreWebView2ProcessFailedKind.RenderProcessExited))
        {
            PageGone?.Invoke(this, reason);
        }

        // M6: the engine itself, or its graphics process, going is what an engine-switch experiment
        // would cause - counted against experiments changed just before (SafetyMarker). A page's own
        // renderer is not: websites crash theirs by themselves.
        if (kind is nameof(CoreWebView2ProcessFailedKind.BrowserProcessExited)
                 or nameof(CoreWebView2ProcessFailedKind.GpuProcessExited))
        {
            SafetyMarker.Problem("its browser engine stopped unexpectedly");
        }

        Diagnostics.Engine(new
        {
            tab = Id,
            kind,
            reason,
            exitCode = Read(() => args.ExitCode),
            description = Read(() => args.ProcessDescription),
            module = Read(() => args.FailureSourceModulePath),
            frames,
            page = IsPrivate ? "(private tab)" : Url,
        }, kind + " " + reason);
    }

    /// <summary>
    /// M5: true for a tab that was opened to fetch something and has not shown a page yet - a link
    /// with target="_blank", or "Download again". If what arrives is a download, the tab has
    /// nothing to show and the window closes it; the first document that commits clears this.
    /// </summary>
    public bool AwaitingFirstPage { get; set; }

    /// <summary>
    /// M5: where downloads land - Settings → Downloads → Location, or the Windows Downloads folder.
    /// It is a property of the engine's PROFILE (the normal windows share one, private windows have
    /// their own), reachable only once this tab's engine exists, so every tab sets it; it costs a
    /// property write.
    /// </summary>
    public void ApplyDownloadFolder()
    {
        try
        {
            if (View.CoreWebView2?.Profile is CoreWebView2Profile profile)
            {
                string folder = MainWindow.DownloadFolder();
                if (!string.Equals(profile.DefaultDownloadFolderPath, folder, StringComparison.OrdinalIgnoreCase))
                {
                    profile.DefaultDownloadFolderPath = folder;
                }
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: download folder");
        }
    }

    /// <summary>
    /// Bug 89 (M5): Edge's own "Downloads" panel opened over Aviary for every download started from a
    /// link with target="_blank" - a separate window of the engine's, so no capture of Aviary's own
    /// window ever showed it; it was found by listing every visible window the engine owned. Handled
    /// in DownloadStarting does not stop it for a tab the engine opened itself. Aviary has its own
    /// list, so whenever the engine opens its panel, it is closed again at once.
    /// </summary>
    private void OnDefaultDownloadDialog(CoreWebView2 sender, object args)
    {
        try
        {
            if (sender.IsDefaultDownloadDialogOpen)
            {
                sender.CloseDefaultDownloadDialog();
                if (Records)
                {
                    Diagnostics.Feature("engine-download-panel-closed", new { tab = Id });
                }
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: close the engine's download panel");
        }
    }

    /// <summary>
    /// Handled = true suppresses WebView2's own download bar, so ours is the only one the
    /// user sees. The download itself goes to the folder <see cref="ApplyDownloadFolder"/> set.
    /// </summary>
    /// <summary>
    /// M5 stage 2 - "Ask where to save each file": set by the window, which owns the Save window.
    /// Given the path the engine would use, answers the path chosen, or null for Cancel.
    /// </summary>
    public Func<string, Task<string?>>? AskWhereToSave { get; set; }

    private async void OnDownloadStarting(CoreWebView2 sender, CoreWebView2DownloadStartingEventArgs args)
    {
        // Taken before the first await or not at all: a deferral asked for later throws, and one
        // never completed leaves the download waiting for ever (the spec's gotcha, and the same
        // shape as NewWindowRequested below).
        Deferral? deferral = null;
        try
        {
            args.Handled = true;
            _downloadStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();

            // M5 stage 2: the download waits, engine-side, while Windows' Save window asks where it
            // goes. Cancel there is a download that never happens - no row, no file.
            if (AppSettings.GetBool("askDownload") && AskWhereToSave is { } ask)
            {
                deferral = args.GetDeferral();
                string? chosen = await ask(args.ResultFilePath ?? string.Empty);
                if (chosen is null)
                {
                    args.Cancel = true;
                    return;
                }

                args.ResultFilePath = chosen;
            }

            if (Records)
            {
                CoreWebView2DownloadOperation op = args.DownloadOperation;
                Diagnostics.Download("start", new
                {
                    tab = Id,
                    file = System.IO.Path.GetFileName(args.ResultFilePath ?? string.Empty),
                    url = op.Uri,
                    mime = op.MimeType,
                    totalBytes = op.TotalBytesToReceive,
                    page = Url,
                });
            }

            DownloadStarted?.Invoke(args.DownloadOperation);
        }
        catch (Exception ex)
        {
            // Let the default UI take over rather than lose the download entirely.
            Diagnostics.Swallowed(ex, "tab: download starting");
        }
        finally
        {
            try { deferral?.Complete(); } catch (Exception ex) { Diagnostics.Swallowed(ex, "tab: download deferral"); }
        }
    }

    /// <summary>
    /// Makes WEB PAGES follow Aviary's theme rather than Windows'. Without this, pinning
    /// the browser to Light leaves every site still reporting prefers-color-scheme: dark,
    /// so the chrome goes light and the page underneath stays black.
    /// </summary>
    public void ApplyColorScheme()
    {
        try
        {
            if (View.CoreWebView2?.Profile is not CoreWebView2Profile profile)
            {
                return;
            }

            profile.PreferredColorScheme = AppSettings.Get("theme") switch
            {
                "Light" => CoreWebView2PreferredColorScheme.Light,
                "Dark" => CoreWebView2PreferredColorScheme.Dark,
                _ => CoreWebView2PreferredColorScheme.Auto
            };
        }
        catch (Exception ex)
        {
            // Older runtimes may not expose the profile; the page just keeps following
            // Windows, which is a cosmetic mismatch rather than a failure.
            Diagnostics.Swallowed(ex, "tab: apply colour scheme");
        }
    }

    /// <summary>
    /// When this tab was last the one being looked at (M4.8). Memory saver sleeps a background
    /// tab only after it has been left alone for a while, so it needs to know how long.
    /// </summary>
    public DateTime LastActiveUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Whether the engine has this tab suspended right now.</summary>
    public bool IsAsleep
    {
        get
        {
            try
            {
                return View.CoreWebView2?.IsSuspended ?? false;
            }
            catch (Exception)
            {
                // Not started yet, or mid-teardown.
                return false;
            }
        }
    }

    /// <summary>
    /// Gentle memory saving: a background tab is told it may release caches and drop its
    /// memory priority. Deliberately NOT TrySuspendAsync — suspension stops timers and
    /// audio, so music in a background tab would die, and the docs are explicit that the
    /// two approaches must not be mixed.
    ///
    /// M4.8: which is why waking comes FIRST. A suspended tab must be resumed before its
    /// memory level is touched again, and before anyone looks at it.
    /// </summary>
    public void SetBackgrounded(bool backgrounded)
    {
        try
        {
            if (View.CoreWebView2 is not CoreWebView2 core)
            {
                return;
            }

            if (!backgrounded)
            {
                Wake();
            }

            // M4.9: memorySaver is a MODE now, not a switch — GetBool on "Balanced" is false, so
            // reading it the old way would have quietly turned the hint off for everyone.
            core.MemoryUsageTargetLevel = backgrounded && MainWindow.Saver != MainWindow.Mode.Off
                ? CoreWebView2MemoryUsageTargetLevel.Low
                : CoreWebView2MemoryUsageTargetLevel.Normal;
        }
        catch (Exception ex)
        {
            // Best effort by design — the API can decline.
            Diagnostics.Swallowed(ex, "tab: memory target level");
        }
    }

    /// <summary>
    /// Wakes a sleeping tab (M4.8). Nothing called Resume before this: the Performance page's
    /// Sleep button has said "wakes when you click it" since M4.5 on the strength of WebView2
    /// resuming a hidden-then-shown view by itself, which is not something to leave a tab's
    /// liveness resting on — and M4.8 puts tabs to sleep on a timer, so it now happens to tabs
    /// nobody chose. Resuming one that is already awake is a no-op.
    /// </summary>
    public void Wake()
    {
        try
        {
            if (View.CoreWebView2 is CoreWebView2 core && core.IsSuspended)
            {
                core.Resume();
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: wake");
        }
    }

    /// <summary>
    /// Puts this tab to sleep if the engine agrees (M4.8). It refuses for a page that is playing
    /// sound or video, or is in the middle of something — that refusal is the safety net that
    /// makes sleeping background tabs on a timer reasonable at all.
    /// </summary>
    public async Task<bool> TrySleepAsync()
    {
        try
        {
            if (View.CoreWebView2 is not CoreWebView2 core || core.IsSuspended)
            {
                return false;
            }

            return await core.TrySuspendAsync();
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: sleep");
            return false;
        }
    }

    /// <summary>
    /// Messages from our own injected scripts, dispatched on their declared type.
    ///
    /// This used to hunt for quote characters around an "action" key with IndexOf. That
    /// worked while there was exactly one message type; adding a second on top of it is
    /// how you get a shortcut firing because an icon message happened to contain the word.
    /// </summary>
    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(args.TryGetWebMessageAsString());
            JsonElement root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out JsonElement typeElement))
            {
                return;
            }

            switch (typeElement.GetString())
            {
                case "shortcut":
                    if (!root.TryGetProperty("token", out JsonElement token)
                        || token.ValueKind != JsonValueKind.String
                        || !string.Equals(token.GetString(), ShortcutToken, StringComparison.Ordinal))
                    {
                        // A page posting a shortcut of its own. Refused, and recorded — the page
                        // address only if this is not a private tab.
                        string source;
                        try { source = args.Source; }
                        catch (Exception ex) { source = string.Empty; Diagnostics.Swallowed(ex, "tab: shortcut message source"); }

                        Diagnostics.Anomaly("untrusted-message", "a website posted a shortcut message",
                            new { handler = "shortcuts", type = "shortcut", source = IsPrivate ? null : source });
                        break;
                    }

                    if (root.TryGetProperty("action", out JsonElement action)
                        && action.GetString() is { Length: > 0 } name)
                    {
                        ShortcutPressed?.Invoke(name);
                    }

                    break;

                case "linkhint":
                    // M5 stage 3: which kind of new-window request the next one is. Token-checked:
                    // a page cannot choose where its own links open.
                    if (root.TryGetProperty("token", out JsonElement hintToken)
                        && hintToken.ValueKind == JsonValueKind.String
                        && string.Equals(hintToken.GetString(), ShortcutToken, StringComparison.Ordinal)
                        && root.TryGetProperty("how", out JsonElement how)
                        && how.GetString() is "background" or "foreground" or "window"
                        && root.TryGetProperty("url", out JsonElement hintUrl))
                    {
                        _linkHint = (how.GetString()!, hintUrl.GetString() ?? string.Empty, System.Diagnostics.Stopwatch.GetTimestamp());
                    }

                    break;

                case "painted":
                    // The injected script saw this document's first paint (BrowserTab.Background.cs).
                    // Token-checked like a shortcut, though all a fake could do is show its own tab early.
                    if (root.TryGetProperty("token", out JsonElement paintToken)
                        && paintToken.ValueKind == JsonValueKind.String
                        && string.Equals(paintToken.GetString(), ShortcutToken, StringComparison.Ordinal))
                    {
                        Unveil();
                    }

                    break;

                case "icons":
                {
                    if (!root.TryGetProperty("links", out JsonElement links)
                        || links.ValueKind != JsonValueKind.Array)
                    {
                        return;
                    }

                    string origin = root.TryGetProperty("origin", out JsonElement o)
                        ? o.GetString() ?? string.Empty
                        : string.Empty;

                    List<IconCandidate> candidates = new();
                    foreach (JsonElement link in links.EnumerateArray())
                    {
                        candidates.Add(new IconCandidate
                        {
                            Href = link.TryGetProperty("href", out JsonElement h) ? h.GetString() ?? "" : "",
                            Rel = link.TryGetProperty("rel", out JsonElement r) ? r.GetString() ?? "" : "",
                            Px = link.TryGetProperty("px", out JsonElement p) && p.TryGetInt32(out int px) ? px : 0,
                            Type = link.TryGetProperty("type", out JsonElement t) ? t.GetString() ?? "" : "",
                        });
                    }

                    if (!IsPrivate)
                    {
                        Favicons.OfferCandidates(IconKey, origin, candidates);
                    }
                    break;
                }

                case "diag":
                    RecordPageError(root, args);
                    break;
            }
        }
        catch (Exception ex)
        {
            // A malformed message from our own injected script is a bug to find in
            // testing, not a reason to take the browser down.
            Diagnostics.Swallowed(ex, "tab: web message");
        }
    }

    /// <summary>
    /// Diagnostics category 8, from <see cref="PageErrorScript"/>.
    ///
    /// ANY page can post a message shaped like this one, so nothing in it is trusted: the page
    /// address comes from the engine (args.Source), not from the message; every string is
    /// length-capped; and a page gets a hundred reports per load before the rest are dropped,
    /// so a page cannot flood the folder.
    /// </summary>
    private void RecordPageError(JsonElement root, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!Records || _pageErrorsThisPage >= 100)
        {
            return;
        }

        _pageErrorsThisPage++;

        string Text(string name, int max)
        {
            if (!root.TryGetProperty(name, out JsonElement v) || v.ValueKind != JsonValueKind.String)
            {
                return string.Empty;
            }

            string s = v.GetString() ?? string.Empty;
            return s.Length > max ? s.Substring(0, max) : s;
        }

        int Number(string name) =>
            root.TryGetProperty(name, out JsonElement v) && v.TryGetInt32(out int n) ? n : 0;

        string kind = Text("kind", 16) switch
        {
            "resource" => "resource",
            "promise" => "promise",
            _ => "js",
        };

        string page;
        try { page = args.Source; }
        catch (Exception ex) { page = Url; Diagnostics.Swallowed(ex, "tab: page-error source"); }

        string? url = NullIfEmpty(Text("url", 2000));

        // A file that "failed" because Aviary's own blocker refused it is not the site being
        // broken, and counting it as one made every ad-funded site look like the worst on the
        // web. It is still recorded — which blocked files a page wanted is exactly how a site
        // BROKEN by the blocker gets diagnosed — but labelled for what it is.
        string? blockedBy = kind == "resource" && url is not null ? BlockedRuleFor(url) : null;

        Diagnostics.PageError(kind, new
        {
            tab = Id,
            page,
            message = NullIfEmpty(Text("message", 1000)),
            source = NullIfEmpty(Text("source", 1000)),
            line = Number("line") is > 0 and var l ? l : (int?)null,
            col = Number("col") is > 0 and var c ? c : (int?)null,
            stack = NullIfEmpty(Text("stack", 3000)),
            url,
            tag = NullIfEmpty(Text("tag", 16)),
            blockedBy,
        });

        static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;
    }

    /// <summary>
    /// Reports a page's script errors, rejected promises and failed files to Aviary. Only
    /// injected while diagnostics is collecting.
    ///
    /// Before anything leaves the page, every value currently typed into the page's fields is
    /// cut out of the message and the stack. A site that throws "invalid value: hunter2" would
    /// otherwise carry what you typed straight into the log — and what you type into forms is
    /// on the never-collected list.
    ///
    /// Main frame only: an embedded frame posts to its own frame object, not to the tab.
    /// </summary>
    private const string PageErrorScript = """
(function(){
  if (window.__aviaryDiag) { return; }
  window.__aviaryDiag = 1;

  var sent = 0;

  function typed(){
    var out = [];
    try {
      var els = document.querySelectorAll('input, textarea');
      for (var i = 0; i < els.length && i < 400; i++) {
        var el = els[i];
        var t = (el.type || '').toLowerCase();
        if (t === 'checkbox' || t === 'radio' || t === 'submit' || t === 'button' ||
            t === 'reset' || t === 'image' || t === 'file' || t === 'range' || t === 'color') { continue; }
        var v = el.value;
        if (v && v.length >= 3) { out.push(v); }
      }
    } catch (e) {}
    out.sort(function(a, b){ return b.length - a.length; });
    return out;
  }

  function clean(s, vals){
    if (s == null) { return ''; }
    s = String(s).slice(0, 3000);
    for (var i = 0; i < vals.length; i++) {
      if (s.indexOf(vals[i]) >= 0) { s = s.split(vals[i]).join('•••'); }
    }
    return s;
  }

  function post(o){
    if (sent >= 60) { return; }
    sent++;
    o.type = 'diag';
    try { window.chrome.webview.postMessage(JSON.stringify(o)); } catch (e) {}
  }

  window.addEventListener('error', function(e){
    var t = e.target;
    if (t && t !== window && t.tagName) {
      post({ kind:'resource', tag:String(t.tagName).toLowerCase(),
             url:String(t.currentSrc || t.src || t.href || '').slice(0, 2000) });
      return;
    }
    var vals = typed();
    post({ kind:'js', message:clean(e.message, vals), source:String(e.filename || '').slice(0, 2000),
           line:e.lineno | 0, col:e.colno | 0, stack:clean(e.error && e.error.stack, vals) });
  }, true);

  window.addEventListener('unhandledrejection', function(e){
    var vals = typed();
    var r = e.reason;
    var message = r && typeof r === 'object' ? (r.message || String(r)) : String(r);
    post({ kind:'promise', message:clean(message, vals), stack:clean(r && r.stack, vals) });
  });
})();
""";

    /// <summary>
    /// The page's own account of how its load went, read once after it completes, while
    /// collecting. The referrer is only knowable from inside the page.
    /// </summary>
    private const string NavigationTimingScript = """
(function(){
  try {
    var r = { referrer: document.referrer || '' };
    var n = performance.getEntriesByType('navigation')[0];
    if (n) {
      r.ttfbMs = Math.round(n.responseStart);
      r.domReadyMs = Math.round(n.domContentLoadedEventEnd);
      r.loadMs = Math.round(n.loadEventEnd);
      r.dnsMs = Math.round(n.domainLookupEnd - n.domainLookupStart);
      r.connectMs = Math.round(n.connectEnd - n.connectStart);
      r.transferBytes = n.transferSize | 0;
      r.protocol = n.nextHopProtocol || '';
    }
    r.resources = performance.getEntriesByType('resource').length;
    return JSON.stringify(r);
  } catch (e) { return '{}'; }
})()
""";

    /// <summary>
    /// Browser shortcuts, caught inside the page and posted up.
    ///
    /// The combo is built from ALL the modifiers before matching. The previous version
    /// bailed out on Alt and ignored Shift entirely, so Ctrl+Shift+D lowercased to "d" and
    /// fired the plain bookmark action — you would add to the Reading List and get a
    /// favourite, and conclude the Reading List was broken.
    ///
    /// e.code, not e.key, for the digits: on a French or German layout the number row
    /// produces punctuation and Ctrl+1 would never match.
    ///
    /// It also reports each top-level document's first paint ({type:'painted'}), because it
    /// already holds the captured postMessage and the token: a new tab is shown only once its
    /// first page has painted (BrowserTab.Background.cs, bug 61).
    /// </summary>
    private const string ShortcutScript = """
(function(){
  if (window.__aviaryKeys) { return; }
  window.__aviaryKeys = 1;

  /* Captured NOW, before any page script exists: a page that later replaces postMessage or
     JSON.stringify must never see the token go past. */
  var wv = window.chrome && window.chrome.webview;
  if (!wv) { return; }
  var send = wv.postMessage.bind(wv);
  var stringify = JSON.stringify;
  var TOKEN = '__AVIARY_KEYTOKEN__';

  /* The page's first paint, once per document: a NEW tab stays invisible until its first page
     has painted, so it never shows a colour that is not the page's (bug 61). */
  if (window === window.top && window.PerformanceObserver) {
    try {
      var painted = false;
      new PerformanceObserver(function(list, observer){
        if (painted) { return; }
        painted = true;
        observer.disconnect();
        try { send(stringify({type:'painted', token:TOKEN})); } catch(err){}
      }).observe({type:'paint', buffered:true});
    } catch(err){}
  }

  var MAP = {
    'f11': 'fullscreen',
    'ctrl+t': 'newtab',
    'ctrl+w': 'closetab',
    'ctrl+l': 'address',
    'ctrl+tab': 'nexttab',
    'ctrl+shift+tab': 'prevtab',
    'ctrl+f': 'find',
    'ctrl+d': 'bookmark',
    'ctrl+shift+d': 'readinglist',
    'ctrl+shift+n': 'privatewindow',
    'ctrl+shift+a': 'searchtabs',
    'ctrl+,': 'settings',
    'ctrl+h': 'history',
    'ctrl+shift+l': 'sidebar',
    'ctrl+r': 'reload',
    'ctrl+j': 'downloads',
    'ctrl+alt+l': 'downloads',
    'ctrl+shift+r': 'reader',
    'ctrl+=': 'zoomin',
    'ctrl++': 'zoomin',
    'ctrl+-': 'zoomout',
    'ctrl+0': 'zoomreset',
    'ctrl+digit1': 'tab1', 'ctrl+digit2': 'tab2', 'ctrl+digit3': 'tab3',
    'ctrl+digit4': 'tab4', 'ctrl+digit5': 'tab5', 'ctrl+digit6': 'tab6',
    'ctrl+digit7': 'tab7', 'ctrl+digit8': 'tab8', 'ctrl+digit9': 'tablast'
  };

  window.addEventListener('keydown', function(e){
    /* A page can dispatch a KeyboardEvent of its own; only real keystrokes count. */
    if (!e.isTrusted || e.metaKey) { return; }

    var key = (e.key || '').toLowerCase();

    // Digits come from e.code so a layout where the number row is punctuation still works.
    if (/^Digit[1-9]$/.test(e.code || '')) { key = (e.code || '').toLowerCase(); }

    var combo = (e.ctrlKey ? 'ctrl+' : '')
              + (e.shiftKey ? 'shift+' : '')
              + (e.altKey ? 'alt+' : '')
              + key;

    var action = MAP[combo];
    if (!action) { return; }

    e.preventDefault();
    e.stopPropagation();
    try { send(stringify({type:'shortcut', action:action, token:TOKEN})); } catch(err){}
  }, true);

  /* M5 stage 3 - WHICH kind of "open elsewhere" a link click meant. The engine already turns a
     Ctrl-, Shift- or middle-click on a link into a new-window request, but WebView2 does not say
     which of the three it was, so every one became a tab in front. This only NOTES the click - it
     never prevents anything: whether a link opens at all is still the page's and the engine's
     business, so a web app that handles its own Ctrl+click is untouched. Capture phase, so a page
     that stops the click travelling cannot hide it; trusted clicks only; carries the token. */
  function linkHint(e){
    if (!e.isTrusted) { return; }
    var middle = e.type === 'auxclick' && e.button === 1;
    if (!middle && (e.type !== 'click' || e.button !== 0)) { return; }
    if (!middle && !e.ctrlKey && !e.shiftKey) { return; }
    var path = e.composedPath ? e.composedPath() : [];
    var link = null;
    for (var i = 0; i < path.length; i++) {
      var n = path[i];
      if (n && (n.tagName === 'A' || n.tagName === 'AREA') && n.href) { link = n; break; }
    }
    if (!link) { return; }
    var how = (middle || e.ctrlKey) ? (e.shiftKey ? 'foreground' : 'background') : 'window';
    try { send(stringify({type:'linkhint', how:how, url:String(link.href), token:TOKEN})); } catch(err){}
  }
  window.addEventListener('click', linkHint, true);
  window.addEventListener('auxclick', linkHint, true);
})();
""";

    /// <summary>
    /// The page zoom, as a CSS zoom on the document element.
    ///
    /// NOT CoreWebView2Controller.ZoomFactor, which is the obvious API and is unreachable
    /// here: WinUI 3's WebView2 element owns its controller privately and never exposes it.
    /// CSS zoom is applied by Chromium at layout, so text reflows properly rather than
    /// being scaled like an image — and it has to be re-applied after every navigation,
    /// because it lives on a document that the next navigation replaces.
    /// </summary>
    public void ApplyZoom()
    {
        try
        {
            if (View.CoreWebView2 is not CoreWebView2 core)
            {
                return;
            }

            // M4.5: Aviary's own pages follow "Text size" in Accessibility, not the zoom people
            // set for websites. Those pages are Settings and the Start Page - the two Aviary
            // writes itself and navigates to with NavigateToHtml. Reader is a website Aviary has
            // restyled, so it keeps the zoom and its own size in the Aa menu.
            string text = IsInternalPage
                ? AppSettings.Get("pageTextSize") switch
                {
                    "Large" => "115",
                    "Larger" => "130",
                    "Largest" => "150",
                    _ => "100",
                }
                : AppSettings.Get("zoom").TrimEnd('%');

            if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double percent)
                || percent < 25 || percent > 500)
            {
                percent = 100;
            }

            string factor = (percent / 100.0).ToString(
                "0.###", System.Globalization.CultureInfo.InvariantCulture);

            _ = core.ExecuteScriptAsync(
                "try{document.documentElement.style.zoom='" + factor + "';}catch(e){}");
        }
        catch (Exception ex)
        {
            // A zoom that will not apply is cosmetic; the page still renders.
            Diagnostics.Swallowed(ex, "tab: apply zoom");
        }
    }

    /// <summary>
    /// M5 stage 4 - a tab from a tab group that has not been looked at yet: it has an address, a
    /// title and its site's icon, and NO engine. Switching to a group with twenty tabs would
    /// otherwise start twenty engines at once (a new tab's engine takes ~355 ms - Grant's own
    /// median); only the tab in front is started, and each of the others when it is first chosen
    /// (MainWindow.RouseAsync). Not the memory saver's sleep: a sleeping tab has an engine, paused.
    /// </summary>
    public bool Dormant { get; private set; }

    /// <param name="url">Empty for a Start Page kept in a tab group.</param>
    public void MakeDormant(string url, string title)
    {
        Dormant = true;
        Url = url;
        Title = string.IsNullOrWhiteSpace(title) ? HostOf(url) : title;
        Monogram = PublicSuffix.Monogram(HostOf(url));
        IconKey = Favicons.KeyFor(url);
        Favicons.Pin(IconKey);
        if (!IsPrivate)
        {
            Diagnostics.TabUrl(Id, url);
            Diagnostics.TabTitle(Id, Title);
        }
    }

    /// <summary>The engine is up: from here the tab is an ordinary one.</summary>
    public void Roused() => Dormant = false;

    public bool CanGoBack => IsReady && View.CanGoBack;

    public bool CanGoForward => IsReady && View.CanGoForward;

    public void Navigate(string url)
    {
        if (IsReady && View.CoreWebView2 is not null)
        {
            Url = url;
            _internalTitle = null;

            // From the address bar, a bookmark or the Start Page: no site started this one,
            // so the privacy step treats it as coming from nowhere (see MaybeRewriteNavigation).
            _hostNavigation = url;
            View.CoreWebView2.Navigate(url);
        }
    }

    public void NavigateToHtml(string html, string displayTitle)
    {
        if (IsReady && View.CoreWebView2 is not null)
        {
            Url = string.Empty;
            Favicons.Unpin(IconKey);
            IconKey = string.Empty;
            Title = displayTitle;
            _internalTitle = displayTitle;
            Monogram = displayTitle.Length > 0 ? displayTitle.Substring(0, 1).ToUpperInvariant() : "•";
            Diagnostics.TabUrl(Id, InternalAddress);
            Diagnostics.TabTitle(Id, displayTitle);
            View.CoreWebView2.NavigateToString(html);
            Changed?.Invoke(this);
        }
    }

    /// <summary>
    /// What diagnostics calls one of Aviary's own pages. Their real address is a data: URI
    /// holding the whole page, which is neither readable nor small.
    /// </summary>
    private string InternalAddress =>
        "aviary:" + (_internalTitle ?? string.Empty).ToLowerInvariant().Replace(' ', '-');

    private void OnSourceChanged(CoreWebView2 sender, CoreWebView2SourceChangedEventArgs args)
    {
        Url = sender.Source;
        IsSecure = Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        UpdateFilterPage(Url);

        if (_internalTitle is null)
        {
            Diagnostics.TabUrl(Id, Url);

            // A single-page app changing route without loading a new document. Navigation
            // lines only cover new documents, so without this YouTube or Gmail would look like
            // one page visited for an hour.
            bool sameDocument;
            try { sameDocument = !args.IsNewDocument; }
            catch (Exception ex) { sameDocument = false; Diagnostics.Swallowed(ex, "tab: IsNewDocument (older runtime?)"); }

            if (sameDocument && Records)
            {
                Diagnostics.Browse("same-document", Url, new { tab = Id, url = Url });
            }
        }

        string previous = IconKey;
        IconKey = Favicons.KeyFor(Url);

        if (!string.Equals(previous, IconKey, StringComparison.OrdinalIgnoreCase))
        {
            // Pinned while open, so the cache never evicts the icon of a site you are
            // actually looking at.
            Favicons.Unpin(previous);
            Favicons.Pin(IconKey);
        }

        Changed?.Invoke(this);
    }

    /// <summary>
    /// Chromium finished decoding a site's icon.
    ///
    /// Fires more than once per page — typically an empty one first, then the real one —
    /// so the read has to be re-checked against the current key after it completes.
    /// </summary>
    private void OnFaviconChanged(CoreWebView2 sender, object args)
    {
        // A private tab leaves no icon behind on disk.
        if (IsPrivate || !Favicons.EngineApiAvailable || !AppSettings.GetBool("siteIcons"))
        {
            return;
        }

        _ = CaptureFaviconAsync(sender, IconKey);
    }

    private static async Task CaptureFaviconAsync(CoreWebView2 core, string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        try
        {
            // IAsyncOperation<IRandomAccessStream> here, NOT Task<Stream>: WinUI 3 consumes
            // the CsWinRT projection, not the WinForms/WPF managed wrapper. A copied WPF
            // sample gives CS1061 on .ReadAsync.
            Windows.Storage.Streams.IRandomAccessStream stream =
                await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);

            if (stream is null || stream.Size == 0 || stream.Size > 256 * 1024)
            {
                return;
            }

            byte[] bytes = new byte[stream.Size];
            using (Stream managed = stream.AsStreamForRead())
            {
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = await managed.ReadAsync(bytes.AsMemory(read, bytes.Length - read));
                    if (n <= 0) { break; }
                    read += n;
                }

                if (read == 0) { return; }
            }

            // The tab may have navigated while that was in flight — without this check the
            // previous site's icon gets stamped onto the new domain.
            if (!string.Equals(key, Favicons.KeyFor(core.Source), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Favicons.Offer(key, bytes, core.Source, IconRank.Engine);
        }
        catch (Exception ex)
        {
            // GetFaviconAsync arrived in WebView2 102. Below that it throws; latch it off
            // once rather than throwing on every page load for the rest of the session.
            //
            // Recorded, because a latch that trips on a CURRENT runtime means every tab
            // silently lost its engine-decoded icon for the rest of the session.
            Diagnostics.Swallowed(ex, "tab: engine favicon (API now latched off)");
            Favicons.MarkEngineApiUnavailable();
        }
    }

    /// <summary>
    /// Reports the icons a page DECLARED, which the engine's own API does not expose.
    /// apple-touch-icon is the crisp one Safari uses for Start Page tiles, and it is
    /// usually the only way to get anything better than 32px.
    /// </summary>
    private const string IconHarvestScript = """
(function(){
  if (window.__aviaryIcons) { return; }
  window.__aviaryIcons = 1;

  function px(el){
    var s = el.getAttribute('sizes') || '';
    if (/any/i.test(s)) { return 1024; }
    var m = /(\d+)\s*[xX]\s*(\d+)/.exec(s);
    if (m) { return parseInt(m[1], 10) || 0; }
    // No declared size. An apple-touch-icon is 180 by convention; a plain icon is small.
    return /apple-touch/i.test(el.getAttribute('rel') || '') ? 180 : 0;
  }

  function harvest(){
    try {
      var out = [];
      var links = document.querySelectorAll(
        'link[rel~="icon" i], link[rel~="shortcut" i], link[rel*="apple-touch" i], link[rel~="mask-icon" i]');
      for (var i = 0; i < links.length && out.length < 12; i++){
        var el = links[i];
        var href = el.getAttribute('href');
        if (!href) { continue; }
        out.push({ href: href, rel: (el.getAttribute('rel')||'').toLowerCase(),
                   px: px(el), type: (el.getAttribute('type')||'').toLowerCase() });
      }
      if (!out.length) { return; }
      window.chrome.webview.postMessage(JSON.stringify(
        { type:'icons', origin: location.origin, links: out }));
    } catch(e){}
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', harvest, { once:true });
  } else {
    harvest();
  }
})();
""";

    private void OnHistoryChanged(CoreWebView2 sender, object args) => Changed?.Invoke(this);

    private void OnDocumentTitleChanged(CoreWebView2 sender, object args)
    {
        if (_internalTitle is not null)
        {
            // Our own pages keep the name we gave them. The start page has a <title> so the
            // WINDOW reads "Aviary"; that should not relabel the tab itself "Aviary".
            Changed?.Invoke(this);
            return;
        }

        string title = sender.DocumentTitle;
        Title = string.IsNullOrWhiteSpace(title) ? HostOf(Url) : title;

        // First letter of the host, not of the title — a monogram should stay put while a
        // single-page app rewrites its title underneath you.
        Monogram = PublicSuffix.Monogram(HostOf(Url));
        if (!IsPrivate)
        {
            History.UpdateTitle(Url, Title);
        }

        Diagnostics.TabTitle(Id, Title);
        LogTitle();

        Changed?.Invoke(this);
    }

    /// <summary>
    /// A page's title usually arrives after its navigation has been logged. Logged once per
    /// change — and a page that retitles itself constantly ("(3) Inbox", "(4) Inbox") gets
    /// one line every few seconds at most.
    /// </summary>
    private void LogTitle()
    {
        if (!Records || string.Equals(Title, _lastTitleLogged, StringComparison.Ordinal))
        {
            return;
        }

        long now = Environment.TickCount64;
        if (now - _lastTitleAt < 5000 && _lastTitleLogged.Length > 0)
        {
            return;
        }

        _lastTitleLogged = Title;
        _lastTitleAt = now;
        Diagnostics.Browse("title", Title, new { tab = Id, url = Url, title = Title });
    }

    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        // A Collection folder opens its index.html, as a web server would. WebView2's folder
        // mapping answers files only, so a bare https://collection.aviary/ — typed, or a link to a
        // folder — was a failed navigation and Aviary's error panel (found in M2.5's first run).
        // The address it was first built under, menagerie.aviary, moves to the new name here too.
        if (Library.RedirectFor(args.Uri) is { } redirect)
        {
            args.Cancel = true;
            sender.Navigate(redirect);
            return;
        }

        // Privacy first. A navigation Aviary replaces with a cleaner one is cancelled here and
        // re-issued, and the replacement comes back through this handler on its own.
        if (MaybeRewriteNavigation(sender, args))
        {
            return;
        }

        // The page's context for the ad blocker — and, in Strict, the warning in place of a site
        // the lists stop as a whole (BrowserTab.Shields.cs).
        if (ShieldsOnNavigationStarting(args))
        {
            return;
        }

        // A navigation of this tab's own is under way: whatever panel it had is over (bug 65).
        // After the Shields check, so Strict's warning — raised in there — is not cleared by the
        // very navigation it stopped.
        NavigationStarted?.Invoke(this);

        IsLoading = true;

        // A web page is loading, so this tab is no longer showing one of Aviary's own pages.
        // Navigate() clears this for the address bar; a page that navigates ITSELF (a link on
        // an internal page, or a test driver) must not leave the tab believing it is still
        // the Start Page — it went on labelling websites "aviary:start-page".
        if (args.Uri.StartsWith("http:", StringComparison.OrdinalIgnoreCase)
            || args.Uri.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
        {
            _internalTitle = null;
        }

        // White under websites, the theme's colour under Aviary's own pages — and when (bug 61).
        CanvasOnNavigationStarting(args);

        // M6: "Make every website dark" - on for a website, off for Aviary's own pages.
        DarkOnNavigationStarting();

        try
        {
            // A redirect arrives as another NavigationStarting with the SAME id, so the chain
            // is built up here and written once, when the navigation completes.
            ulong id = args.NavigationId;
            if (id != _navId || !args.IsRedirected)
            {
                _navId = id;
                _navStart = System.Diagnostics.Stopwatch.GetTimestamp();
                _navChain.Clear();
                _navUserInitiated = args.IsUserInitiated;
                _pageErrorsThisPage = 0;
                _lastTitleLogged = string.Empty;

                try { _navKind = args.NavigationKind.ToString(); }
                catch (Exception ex) { _navKind = string.Empty; Diagnostics.Swallowed(ex, "tab: NavigationKind (older runtime?)"); }
            }

            if (Records && _navChain.Count < 20)
            {
                _navChain.Add(args.Uri);
            }

            _prevUriInNavigation = args.Uri;

            // The replacement HTTPS attempt has started: remember its id, and give it a
            // deadline — a server that silently drops port 443 would otherwise hang for
            // twenty seconds before Windows gives up on the connection.
            if (_upgrade is { NavId: 0 } up && string.Equals(args.Uri, up.Https, StringComparison.Ordinal))
            {
                _upgrade = up with { NavId = args.NavigationId };
                StartUpgradeDeadline();
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: navigation starting");
        }

        Changed?.Invoke(this);
    }

    private void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        IsLoading = false;
        ApplyZoom();
        CanvasOnNavigationCompleted(args);

        // A navigation that never committed (cancelled, a download, a failure) leaves the page
        // that was already showing — and that is the page its requests belong to.
        if (!args.IsSuccess)
        {
            _mainDocument = DocumentKey(Url);
            UpdateFilterPage(Url);
        }

        Changed?.Invoke(this);

        if (Interlocked.Exchange(ref _firstPage, 1) == 0)
        {
            Diagnostics.Mark("first-page-loaded");
        }

        // Read before anything can move on: the next navigation overwrites both.
        long navStart = _navStart;
        ulong navId = args.NavigationId;
        CoreWebView2WebErrorStatus status = args.WebErrorStatus;

        // A navigation the privacy step replaced with a cleaner one: its "cancelled" ending is
        // expected, and the replacement is already on its way.
        if (!args.IsSuccess && navId == _replacedNavId && status == CoreWebView2WebErrorStatus.OperationCanceled)
        {
            return;
        }

        // An HTTPS-Only attempt that found no secure site hands over to the fallback panel.
        bool httpsFallback = FinishUpgrade(args);

        // The one failure a download also produces (see _downloadStartedAt).
        bool mayBeDownload = !args.IsSuccess && status == CoreWebView2WebErrorStatus.ConnectionAborted;

        if (Records)
        {
            _ = LogNavigationAsync(sender, args, navStart, mayBeDownload);
        }

        // Once per successful navigation. The title is usually still the old one here, so
        // OnDocumentTitleChanged corrects it WITHOUT counting a second visit.
        if (args.IsSuccess && _internalTitle is null)
        {
            if (!IsPrivate)
            {
                History.Record(Url, Title);
            }

            Navigated?.Invoke(this);
        }

        // A SERVER that answered with 404 or 500 sent a page of its own — "not found", a
        // search box, a way back — and WebView2 still reports that as a failed navigation with
        // status Unknown. Covering it with Aviary's "This page didn't load" panel hid the site's
        // own page behind a vaguer message. Found by diagnostics in M1 (the error-page-shown
        // event fired on a 404). Aviary's panel is for when nothing answered at all.
        if (args.IsSuccess
            || httpsFallback
            || status == CoreWebView2WebErrorStatus.OperationCanceled
            || ServerAnswered(args))
        {
            return;
        }

        if (mayBeDownload)
        {
            _ = ReportFailureUnlessDownloadAsync(status, navId, navStart);
        }
        else
        {
            ShowingFailure();
            NavigationFailed?.Invoke(this, status);
        }
    }

    /// <summary>
    /// An aborted navigation is only a failure if it did NOT become a download. Waits briefly
    /// for DownloadStarting, which this runtime raises after NavigationCompleted; stays quiet if
    /// one arrives, or if the tab has moved on to another navigation meanwhile.
    /// </summary>
    private async Task ReportFailureUnlessDownloadAsync(CoreWebView2WebErrorStatus status, ulong navId, long navStart)
    {
        try
        {
            if (!DownloadStartedSince(navStart))
            {
                await Task.Delay(DownloadGraceMs);
            }

            if (DownloadStartedSince(navStart) || _navId != navId || !IsReady)
            {
                return;
            }

            ShowingFailure();
            NavigationFailed?.Invoke(this, status);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: report failed navigation");
        }
    }

    /// <summary>
    /// Nothing secure is on screen when Aviary's own failure panel covers the page, so the
    /// padlock goes. It used to stay: the https address that FAILED was still the tab's
    /// address, and the HTTPS-Only fallback panel ("this site doesn't offer a secure
    /// connection") sat under a padlock (found in M2's first look at that panel).
    /// </summary>
    private void ShowingFailure()
    {
        if (IsSecure)
        {
            IsSecure = false;
            Changed?.Invoke(this);
        }
    }

    private static bool ServerAnswered(CoreWebView2NavigationCompletedEventArgs args)
    {
        try
        {
            return args.WebErrorStatus == CoreWebView2WebErrorStatus.Unknown && args.HttpStatusCode > 0;
        }
        catch (Exception ex)
        {
            // HttpStatusCode is newer than the rest of these arguments.
            Diagnostics.Swallowed(ex, "tab: HttpStatusCode for error page");
            return false;
        }
    }

    /// <summary>
    /// Diagnostics categories 1 and 13: where this navigation went and how it went. Everything
    /// that can change is read BEFORE the first await — another navigation may begin while the
    /// page's timing is being fetched.
    /// </summary>
    private async Task LogNavigationAsync(CoreWebView2 core, CoreWebView2NavigationCompletedEventArgs args,
        long navStart, bool mayBeDownload)
    {
        try
        {
            bool success = args.IsSuccess;
            bool download = DownloadStartedSince(navStart);
            bool internalPage = _internalTitle is not null;

            int? httpStatus = null;
            try { httpStatus = args.HttpStatusCode is > 0 and var code ? code : null; }
            catch (Exception ex) { Diagnostics.Swallowed(ex, "tab: HttpStatusCode (older runtime?)"); }

            CoreWebView2WebErrorStatus status = args.WebErrorStatus;

            double ms = navStart == 0
                ? 0
                : Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(navStart).TotalMilliseconds, 1);

            string url = internalPage ? InternalAddress : core.Source;
            List<string>? redirects = _navChain.Count > 1 ? new List<string>(_navChain) : null;
            bool userInitiated = _navUserInitiated;
            string kind = _navKind;

            // Everything mutable is captured; now it is safe to wait for a late DownloadStarting.
            if (mayBeDownload && !download)
            {
                await Task.Delay(DownloadGraceMs);
                download = DownloadStartedSince(navStart);
            }

            // "Unknown" is what the engine calls a server's own error page; say what it was.
            string result = download ? "became a download"
                          : success ? "ok"
                          : status == CoreWebView2WebErrorStatus.Unknown && httpStatus is > 0
                              ? "HTTP " + httpStatus
                              : status.ToString();

            JsonElement? timing = null;
            if (success && !internalPage && !download)
            {
                try
                {
                    // A page too busy to answer within three seconds still gets its line.
                    Task<string> script = core.ExecuteScriptAsync(NavigationTimingScript).AsTask();
                    if (await Task.WhenAny(script, Task.Delay(3000)) == script)
                    {
                        string? inner = JsonSerializer.Deserialize<string>(await script);
                        if (!string.IsNullOrEmpty(inner))
                        {
                            using JsonDocument doc = JsonDocument.Parse(inner);
                            timing = doc.RootElement.Clone();
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Usually the page moved on before it could answer. The navigation line is
                    // still written below, just without the page's own timings.
                    Diagnostics.Swallowed(ex, "tab: read page timing");
                }
            }

            string title = internalPage ? (_internalTitle ?? string.Empty) : core.DocumentTitle;

            string? Str(string name) =>
                timing is { } t && t.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString()
                    : null;

            long? Num(string name) =>
                timing is { } t && t.TryGetProperty(name, out JsonElement v) && v.TryGetInt64(out long n)
                    ? n
                    : null;

            Diagnostics.Browse("navigation", url, new
            {
                tab = Id,
                url,
                title,
                result,
                httpStatus,
                ms,
                redirects,
                referrer = Str("referrer") is { Length: > 0 } r ? r : null,
                userInitiated,
                kind = kind.Length > 0 ? kind : null,
            });

            if (!internalPage && success)
            {
                string site = NetRecord.SiteOf(url);
                Diagnostics.TrackSlow("navigation", ms, site);
                Diagnostics.Perf("navigation", new
                {
                    tab = Id,
                    site,
                    ms,
                    ttfbMs = Num("ttfbMs"),
                    domReadyMs = Num("domReadyMs"),
                    loadMs = Num("loadMs"),
                    dnsMs = Num("dnsMs"),
                    connectMs = Num("connectMs"),
                    transferBytes = Num("transferBytes"),
                    resources = Num("resources"),
                    protocol = Str("protocol"),
                });
            }
        }
        catch (Exception ex) when (IsClosingWebView(ex))
        {
            // Bug 90 (M5, from Grant's own diagnostics, 24 Sep 22:15): the tab closed while its
            // finished navigation was being written down, so the engine answered "going away" for
            // the page title. Bug 79's rule - a tab closing is not a fault - had one call site; the
            // navigation log is the second. The navigation was real; its record is simply not made.
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: log navigation");
        }
    }

    /// <summary>
    /// Asks the host for a fresh, initialised, un-navigated CoreWebView2 to hand a new
    /// window to. Null means the host could not make one.
    /// </summary>
    /// <summary>
    /// Raised so the window can build an EMPTY, initialised, un-navigated tab for a new-window
    /// request - "tab" (in front), "background", or "window" (M5 stage 3: from the click's hint).
    /// </summary>
    public Func<string, Task<CoreWebView2?>>? BlankTabRequested { get; set; }

    /// <summary>The last link click with Ctrl, Shift or the middle button: how, where, and when.</summary>
    private (string How, string Url, long At)? _linkHint;

    /// <summary>
    /// What the user's click asked for, if this new-window request came from one. The hint and the
    /// request travel separately from the page, so the hint may land a moment AFTER the request:
    /// for a request the user made, a short wait (at most ~120 ms) is allowed for it.
    /// </summary>
    private async Task<string> HowToOpenAsync(CoreWebView2NewWindowRequestedEventArgs args)
    {
        if (!args.IsUserInitiated)
        {
            return "tab";
        }

        string wanted = Normalise(args.Uri);
        for (int waited = 0; ; waited += 15)
        {
            if (_linkHint is { } hint)
            {
                double age = System.Diagnostics.Stopwatch.GetElapsedTime(hint.At).TotalMilliseconds;
                // The link that was clicked - or, failing an exact match, a click this instant
                // (a page may open something other than the href; Chromium keys that off the
                // click's modifiers too).
                if (age < 1500 && (Normalise(hint.Url) == wanted || age < 400))
                {
                    _linkHint = null;
                    return hint.How;
                }
            }

            if (waited >= 120)
            {
                return "tab";
            }

            await Task.Delay(15);
        }

        static string Normalise(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out Uri? u) ? u.GetLeftPart(UriPartial.Query) : url;
    }

    /// <summary>
    /// target="_blank", window.open(), Ctrl+click and middle-click.
    ///
    /// This used to call sender.Navigate(args.Uri) — the new page REPLACED the one you
    /// were reading, which is wrong in a way you notice within a minute of real browsing.
    /// Opening a genuine tab means handing the engine a second CoreWebView2, and creating
    /// one is asynchronous, so the event has to be deferred: without the deferral the
    /// handler returns before NewWindow is assigned and the engine falls back to its own
    /// window.
    ///
    /// The engine navigates the child itself. Calling Navigate on it here would load the
    /// page twice and leave a bogus first entry in the new tab's back history.
    /// </summary>
    private async void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;

        // Take the deferral SYNCHRONOUSLY. Anything after the first await is too late —
        // the event has already returned by then.
        //
        // Windows.Foundation.Deferral, NOT CoreWebView2Deferral: that name belongs to the
        // .NET wrapper in the net462 lib, and this project binds the WinRT projection
        // (Microsoft.Web.WebView2.Core.winmd), where every deferral is the standard one.
        Deferral deferral = args.GetDeferral();

        try
        {
            // A window the user did not ask for is a pop-up. One they did — a click, a
            // Ctrl+click, a middle-click — always opens, which is the distinction the
            // setting's description promises.
            if (!args.IsUserInitiated && AppSettings.GetBool("popups"))
            {
                return;
            }

            if (BlankTabRequested is not { } request)
            {
                return;
            }

            if (await request(await HowToOpenAsync(args)) is CoreWebView2 blank)
            {
                args.NewWindow = blank;
            }
        }
        catch (Exception ex)
        {
            // Losing the new window is survivable; an unhandled exception on an async void
            // handler is not — it comes back as a process-level crash.
            Diagnostics.Swallowed(ex, "tab: open new window");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnWindowCloseRequested(CoreWebView2 sender, object args) => CloseRequested?.Invoke(this);

    /// <summary>
    /// Is this the engine saying "that WebView is going away" rather than something wrong? (M4.9
    /// bug 79, shared since M5 bug 90.) WebView2 answers ERROR_INVALID_STATE (0x8007139F) for a
    /// CoreWebView2 whose host is being torn down. Matched on that HRESULT and nothing else, so a
    /// real failure still reports - and used only where a closing tab is the whole explanation.
    /// </summary>
    internal static bool IsClosingWebView(Exception ex) =>
        ex is System.Runtime.InteropServices.COMException com && unchecked((uint)com.HResult) == 0x8007139FU;

    public static string HostOf(string url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            string host = uri.Host;
            return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host.Substring(4) : host;
        }

        return url;
    }

    /// <summary>
    /// Unsubscribe, then close. Relying on collection leaves the renderer process alive —
    /// close ten tabs without this and ten renderers stay resident.
    /// </summary>
    /// <summary>
    /// M5: the tab was closed while a download it started is still running. In WebView2 a
    /// download belongs to the engine of the page that started it and dies the moment that engine
    /// is closed (measured: frozen at 0 bytes, and the engine never said so) - so the tab leaves
    /// the strip but its engine is kept, hidden and muted, until the download ends. Chrome closes
    /// such a tab at once and the download carries on; this is how that looks from outside.
    /// Everything the window listened to is let go, so a parked page cannot open tabs or panels.
    /// </summary>
    public void Park()
    {
        try
        {
            if (View.CoreWebView2 is CoreWebView2 core)
            {
                core.IsMuted = true;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: park");
        }

        View.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        BlankTabRequested = null;
        ReleaseHostEvents();
    }

    public void Dispose()
    {
        // M5: a download this tab was running dies with its engine. Say so in the list, rather
        // than leave a row that claims to be downloading for ever.
        Downloads.OwnerGone(Id);

        try
        {
            if (View.CoreWebView2 is CoreWebView2 core)
            {
                core.SourceChanged -= OnSourceChanged;
                core.HistoryChanged -= OnHistoryChanged;
                core.DocumentTitleChanged -= OnDocumentTitleChanged;
                core.FaviconChanged -= OnFaviconChanged;
                core.NavigationStarting -= OnNavigationStarting;
                core.ContentLoading -= OnContentLoading;
                core.NavigationCompleted -= OnNavigationCompleted;
                core.NewWindowRequested -= OnNewWindowRequested;
                core.WindowCloseRequested -= OnWindowCloseRequested;
                core.WebMessageReceived -= OnWebMessageReceived;
                core.WebResourceRequested -= OnWebResourceRequested;
                core.DownloadStarting -= OnDownloadStarting;
                core.IsDefaultDownloadDialogOpenChanged -= OnDefaultDownloadDialog;
                core.ProcessFailed -= OnProcessFailed;
                core.ContainsFullScreenElementChanged -= OnContainsFullScreenElementChanged;
                core.WebResourceResponseReceived -= OnWebResourceResponseReceived;
            }

            Favicons.Unpin(IconKey);
            View.Close();
        }
        catch (Exception ex)
        {
            // Already gone.
            Diagnostics.Swallowed(ex, "tab: dispose");
        }

        // A static event holds its listeners: without this every closed tab would be kept
        // alive by the diagnostics switch for the rest of the session.
        Diagnostics.EnabledChanged -= OnDiagnosticsChanged;
        DetachShields();
        Diagnostics.TabClosed(Id);
        _inFlight.Clear();
        ReleaseHostEvents();
        IsReady = false;
    }

    private void ReleaseHostEvents()
    {
        Changed = null;
        CloseRequested = null;
        ShortcutPressed = null;
        DownloadStarted = null;
        NavigationFailed = null;
        Navigated = null;
        FullScreenChanged = null;
    }
}
