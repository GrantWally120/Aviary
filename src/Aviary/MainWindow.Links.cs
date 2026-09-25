using System;
using System.Threading.Tasks;

namespace Aviary;

/// <summary>
/// Where a link goes when it is not simply followed (Alpha Build 5 M5 stage 3).
///
/// <para><b>What was measured first.</b> The engine already turns a modified click on a link into
/// the right KIND of request — Chromium works it out from the click (Ctrl or the middle button: a
/// tab behind; Ctrl+Shift: a tab in front; Shift: a new window; Alt: save the file) — and Alt+click
/// already downloaded, across sites too. What was wrong was only Aviary's answer: WebView2 reports
/// every one of the first three as the same new-window request, so every one became a tab in front.
/// The recovered spec planned a script that took over those clicks; measured, it only needs to
/// NOTE them (<c>linkhint</c> in <see cref="BrowserTab"/>'s shortcut script), so the page and the
/// engine still decide whether anything opens at all — a web app's own Ctrl+click is untouched.</para>
///
/// <para><b>Background tabs</b> go next to the tab they came from, one after another in the order
/// they were opened (Chrome's and Safari's order), as long as "New tab position" says after the
/// current tab. <b>A link window</b> holds just that link's tab; it restores no pinned tabs and
/// never saves them (<see cref="SavePinned"/>), because its set is not the set.</para>
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>This window was opened for one link (Shift+click, or "Open Link in New Window").</summary>
    private readonly bool _linkWindow;

    /// <summary>A link window's one tab, handed to whoever opened the window once its engine exists.</summary>
    private readonly TaskCompletionSource<BrowserTab?> _linkTab = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The tab the last background tabs were opened from, and how many followed it.</summary>
    private BrowserTab? _childOf;
    private int _children;

    /// <summary>
    /// Where the next background tab goes: after the tab it was opened from and after the ones
    /// already opened from it — or at the end, when "New tab position" says so.
    /// </summary>
    private int BackgroundSlot()
    {
        if (_active is not { } from
            || !AppSettings.Get("newTabPosition").StartsWith("After", StringComparison.OrdinalIgnoreCase))
        {
            return _tabs.Count;
        }

        if (!ReferenceEquals(_childOf, from))
        {
            _childOf = from;
            _children = 0;
        }

        int slot = Math.Max(_pinned.Count, _tabs.IndexOf(from) + 1 + _children);
        _children++;
        return Math.Min(slot, _tabs.Count);
    }

    /// <summary>
    /// A new window of the same kind (a private one from a private window) holding one empty tab,
    /// returned once its engine exists. The caller either hands it to the engine (Shift+click) or
    /// navigates it ("Open Link in New Window").
    /// </summary>
    private async Task<BrowserTab?> OpenLinkWindowAsync()
    {
        try
        {
            MainWindow window = new(isPrivate: _private, forLink: true);
            window.Activate();

            Task<BrowserTab?> ready = window._linkTab.Task;
            Task finished = await Task.WhenAny(ready, Task.Delay(TimeSpan.FromSeconds(15)));
            return finished == ready ? await ready : null;
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "links: open a new window");
            return null;
        }
    }

    /// <summary>Right-click → "Open Link in New Tab": behind this one, as Safari and Chrome do.</summary>
    private async Task OpenLinkInBackgroundAsync(string url)
    {
        DiagUi("link-open", new { how = "background", from = "menu" });
        BrowserTab? tab = await CreateTabAsync(select: false, reason: "link opened a background tab", slot: BackgroundSlot());
        if (tab is not null)
        {
            tab.AwaitingFirstPage = true;
            tab.Navigate(url);
            RebuildTabStrip();
        }
    }

    /// <summary>Right-click → "Open Link in New Window".</summary>
    private async Task OpenLinkInWindowAsync(string url)
    {
        DiagUi("link-open", new { how = "window", from = "menu" });
        if (await OpenLinkWindowAsync() is { } tab)
        {
            tab.Navigate(url);
        }
    }
}
