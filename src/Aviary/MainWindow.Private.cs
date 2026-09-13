using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;

namespace Aviary;

/// <summary>
/// A private window (Phase 5 M2).
///
/// A second MainWindow in the same process, every tab of it on WebView2's InPrivate
/// (off-the-record) profile: cookies, cache and site storage live in memory and are thrown
/// away when the window's last tab closes. And Aviary itself keeps nothing about it — no
/// history, no favicons on disk, no pinned tabs, no Privacy Report counts, no HTTPS
/// exceptions in sites.json, and NO diagnostics: every record a MainWindow makes goes
/// through the Diag* methods below, which a private window turns into nothing. Files it
/// downloads are kept, as in every browser, and bookmarks or reading-list items you add on
/// purpose are kept too.
///
/// Why a window rather than private tabs in the normal window: Safari, Chrome and Brave all
/// make it a window, so the boundary is something you can see — the whole window is private
/// or none of it is.
/// </summary>
public sealed partial class MainWindow
{
    private readonly bool _private;

    /// <summary>Every window that is open, so the LAST to close — whichever it is — shuts down.</summary>
    private static readonly List<MainWindow> OpenWindows = new();

    private void OpenPrivateWindow()
    {
        // The fact that one was opened, and nothing about what happens in it.
        Diagnostics.Ui("private-window-open", new { windows = OpenWindows.Count + 1 });

        MainWindow window = new(isPrivate: true);
        window.Activate();
    }

    // ══ DIAGNOSTICS, PER WINDOW ════════════════════════════════════════════
    //
    // "Private windows are never recorded" (PHASE5-PLAN.md, M1). Until M2 that rested on a
    // global setting nothing ever set, and M2's check found three kinds of record — Reader's
    // verdict, the error panel, downloads — carrying full addresses past it (bug 40). Now a
    // private window's own records simply do not happen.

    private void DiagUi(string name, object? data = null)
    {
        if (!_private)
        {
            Diagnostics.Ui(name, data);
        }
    }

    private void DiagFeature(string name, object? data = null)
    {
        if (!_private)
        {
            Diagnostics.Feature(name, data);
        }
    }

    private void DiagBrowse(string kind, string crumb, object data)
    {
        if (!_private)
        {
            Diagnostics.Browse(kind, crumb, data);
        }
    }

    private void DiagPerf(string kind, object data)
    {
        if (!_private)
        {
            Diagnostics.Perf(kind, data);
        }
    }

    private void DiagTrackSlow(string kind, double ms, string what)
    {
        if (!_private)
        {
            Diagnostics.TrackSlow(kind, ms, what);
        }
    }

    /// <summary>
    /// A private window's look: a "Private" badge in the toolbar and the word in the window
    /// title, so it can never be mistaken for the normal window.
    /// </summary>
    private void ApplyPrivateChrome()
    {
        PrivateBadge.Visibility = _private ? Visibility.Visible : Visibility.Collapsed;
    }
}
