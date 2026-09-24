using System;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// The colour the engine paints where a page paints nothing (bug 61): under a page that sets no
/// background of its own, and in a tab before its first page has drawn.
///
/// Until 13 Sep 2026 it was the theme's page colour under every page — Phase 1 chose that against
/// a white flash in the dark theme, and M0 made it the pinned theme's rather than Windows' (bug 25)
/// — so in the dark theme a page that sets no colours (a plain test page, a text file, an old site)
/// came out as black text on dark grey. Chrome and Safari put such a page on white and leave dark
/// to the page: one that declares <c>color-scheme: dark</c> gets the engine's own dark canvas
/// (#121212) from Chromium itself, as long as this colour is opaque. So now:
///
///   - under a WEBSITE: white, whatever Aviary's theme;
///   - under Aviary's OWN pages (Start Page, Settings), and in a tab that has shown nothing yet:
///     the theme's page colour, so a new tab never flashes white before its Start Page draws.
///
/// WHEN it changes is the whole difficulty, because the colour shows wherever the page on screen
/// paints nothing:
///   - To white, for a website on its way: at once in NavigationStarting if the tab shows one of
///     Aviary's own pages — they paint their whole background, so the change is hidden under them
///     and the website's first frame is drawn on white. A tab that has shown nothing has no page
///     to hide it, so there the change waits for the website to commit (ContentLoading).
///   - To the theme's colour, for one of Aviary's pages on its way: at ContentLoading, once the
///     page it replaces has gone. Any earlier and a plain website turns dark under its own text for
///     as long as the next page takes to arrive.
///
/// Measured in the running app before this was written (tools/test/bg-battery.ps1): between two
/// pages Chromium keeps the old one on screen until the new one has painted — a stylesheet held
/// back 1.6 s showed the old page for 1.6 s, never this colour — so within a tab it is never seen
/// as a flash.
///
/// A NEW tab has no old page to keep. Its first page shows this colour from the moment it commits
/// until it paints — which for a site with a slow stylesheet was 1.5 s of white before a dark page,
/// in the dark theme (the flash Phase 1 set the theme colour against) — and before that the engine
/// shows colours of its own that no setting reaches: #202020 / #F3F3F3, then #121212 in the dark
/// theme, and one white frame when a link opens the tab. Nothing can know before the first paint
/// whether a page paints its own background, so a new tab is simply not shown until it has
/// painted: its WebView starts at Opacity 0 over ContentHost, whose background is the theme's page
/// colour, and <see cref="Unveil"/> shows it when the injected script reports the first paint —
/// or a quarter of a second after a committed navigation ends (a page that paints nothing, or one
/// loaded out of sight, reports no paint), or when the renderer dies, or after five seconds
/// whatever happened.
/// </summary>
internal sealed partial class BrowserTab
{
    private enum Canvas { Nothing, Aviary, Website }

    /// <summary>What the document on screen is, as of the last one to commit.</summary>
    private Canvas _canvas = Canvas.Nothing;

    /// <summary>
    /// The navigation heading for a website, from its NavigationStarting until it commits or ends.
    /// While the tab shows one of Aviary's pages, this is what turns the colour white early.
    /// </summary>
    private ulong? _websiteComing;

    /// <summary>The theme's page colour (AvPageBrush, resolved by MainWindow against the theme on screen).</summary>
    private Windows.UI.Color _themePage = Microsoft.UI.Colors.White;

    private Windows.UI.Color? _canvasApplied;

    /// <summary>The tab's WebView has been shown — its first page painted, or a fallback fired. Never hidden again.</summary>
    private bool _unveiled;

    /// <summary>How long a new tab may stay invisible, whatever happens to its first page.</summary>
    private const int UnveilDeadlineMs = 5000;

    /// <summary>How long a finished page may still take to report its first paint before it is shown anyway.</summary>
    private const int UnveilGraceMs = 250;

    /// <summary>The theme changed, or this tab is new: the colour Aviary's own pages sit on.</summary>
    public void SetThemePageColour(Windows.UI.Color colour)
    {
        _themePage = colour;
        ApplyCanvasColour();
    }

    private void ApplyCanvasColour(bool force = false)
    {
        bool white = _canvas == Canvas.Website || (_canvas == Canvas.Aviary && _websiteComing is not null);
        Windows.UI.Color colour = white ? Microsoft.UI.Colors.White : _themePage;

        if (!force && _canvasApplied == colour)
        {
            return;
        }

        try
        {
            View.DefaultBackgroundColor = colour;
            _canvasApplied = colour;
        }
        catch (Exception ex)
        {
            // A wrong colour under a page is survivable; a crash is not.
            Diagnostics.Swallowed(ex, "tab: page background colour");
        }
    }

    /// <summary>From OnNavigationStarting, once the navigation is going ahead.</summary>
    private void CanvasOnNavigationStarting(CoreWebView2NavigationStartingEventArgs args)
    {
        // OnNavigationStarting has just cleared _internalTitle for any http(s) address, and
        // NavigateToHtml set it before its NavigateToString — so null here means a website.
        _websiteComing = _internalTitle is null ? args.NavigationId : null;
        ApplyCanvasColour();
    }

    /// <summary>A new document committed: the page it replaced is gone, so its colour can go too.</summary>
    private void OnContentLoading(CoreWebView2 sender, CoreWebView2ContentLoadingEventArgs args)
    {
        // M5: a document committed, so this tab has shown a page of its own (a download never does).
        AwaitingFirstPage = false;
        _canvas = _internalTitle is null ? Canvas.Website : Canvas.Aviary;
        _websiteComing = null;
        ApplyCanvasColour();
    }

    /// <summary>A navigation that never committed (cancelled, a download, refused) leaves the page that was showing.</summary>
    private void CanvasOnNavigationCompleted(CoreWebView2NavigationCompletedEventArgs args)
    {
        // By id: a navigation the privacy step replaced ends AFTER its replacement has started.
        if (_websiteComing == args.NavigationId)
        {
            _websiteComing = null;
            ApplyCanvasColour();
        }

        // A document that committed and finished without reporting a paint — an empty page, a
        // page loaded while its tab was out of sight (Chromium reports no first paint for those),
        // an error page under Aviary's panel — is still what the tab should show. After a grace
        // period: a page with nothing to fetch (Settings) finishes loading BEFORE its first frame,
        // and showing it then showed the engine's blank #121212 for a frame or two.
        if (_canvas != Canvas.Nothing && !_unveiled)
        {
            _ = UnveilAfterAsync(UnveilGraceMs);
        }
    }

    /// <summary>Shows a new tab's WebView, once. See the summary at the top.</summary>
    private void Unveil()
    {
        if (_unveiled)
        {
            return;
        }

        _unveiled = true;
        View.Opacity = 1;
    }

    private async Task UnveilAfterAsync(int ms)
    {
        await Task.Delay(ms);
        Unveil();
    }
}
