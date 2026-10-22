using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// Quik keeps a Start Page ready, outside the tab strip. Quik Labs can keep a second
/// one for two quick opens. Each window owns its pool, including private windows.
/// </summary>
public sealed partial class MainWindow
{
    public const string QuikReadyTabKey = "quikReadyTab";
    private const string QuikTwoReadyKey = "exp.quikTwoReady";
    private static readonly TimeSpan ReadyTabFreshFor = TimeSpan.FromMinutes(10);

    private readonly List<BrowserTab> _readyTabs = new();
    private readonly Dictionary<BrowserTab, DateTime> _readyTabBuilt = new();
    private BrowserTab? _warmingReadyTab;
    private bool _readyTabWarming;
    private int _readyGeneration;
    private bool _quikClosing;
    private DispatcherQueueTimer? _readyTabTimer;

    private bool ReadyTabWanted => Quik.On && !_quikClosing && !_linkWindow
        && AppSettings.GetBool(QuikReadyTabKey) && OpenWindows.Contains(this);
    private int ReadyTabCapacity => Quik.On && AppSettings.GetBool(QuikTwoReadyKey) ? 2 : 1;
    private bool IsQuikReadyTab(BrowserTab tab) =>
        _readyTabs.Contains(tab) || ReferenceEquals(tab, _warmingReadyTab);

    private void QueueReadyTab()
    {
        if (!ReadyTabWanted || _readyTabs.Count >= ReadyTabCapacity || _readyTabWarming)
        {
            return;
        }
        _readyTabWarming = true;
        int generation = _readyGeneration;
        bool queued = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, async () =>
        {
            bool made = false;
            try
            {
                await Task.Delay(350);
                if (generation == _readyGeneration && ReadyTabWanted && _readyTabs.Count < ReadyTabCapacity)
                {
                    made = await WarmReadyTabAsync(generation);
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Swallowed(ex, "quik: queue a ready tab");
            }
            finally
            {
                _readyTabWarming = false;
                // A switch changed during creation, or Labs still needs its second tab.
                // Do not spin forever when the engine cannot create a tab.
                if (made || generation != _readyGeneration) { QueueReadyTab(); }
            }
        });
        if (!queued) { _readyTabWarming = false; }
        if (_readyTabTimer is null)
        {
            _readyTabTimer = DispatcherQueue.CreateTimer();
            _readyTabTimer.Interval = TimeSpan.FromMinutes(1);
            _readyTabTimer.Tick += (_, _) => { RefreshReadyTabsIfStale(); QueueReadyTab(); };
        }
        _readyTabTimer.Start();
    }

    private async Task<bool> WarmReadyTabAsync(int generation)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        BrowserTab tab = new(_private);
        _warmingReadyTab = tab;
        bool kept = false;
        try
        {
            WireTab(tab);
            tab.SetThemePageColour(ThemePageColour());
            tab.DeferShieldsScripts = true;
            ContentHost.Children.Add(tab.View);
            await tab.InitialiseAsync();
            if (generation != _readyGeneration || !ReadyTabWanted) { return false; }
            TrackFocusHandoff(tab);
            tab.SetBackgrounded(true);
            tab.SetInBackground(true);
            HookContextMenu(tab);
            CoreWebView2 core = tab.View.CoreWebView2
                ?? throw new InvalidOperationException("The ready tab has no engine.");
            core.WebMessageReceived += StartPage_WebMessageReceived;
            TaskCompletionSource<bool> loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnLoaded(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args) =>
                loaded.TrySetResult(args.IsSuccess);
            core.NavigationCompleted += OnLoaded;
            try
            {
                tab.NavigateToHtml(BuildStartPageHtml(), "Start Page");
                if (await Task.WhenAny(loaded.Task, Task.Delay(5000)) != loaded.Task || !await loaded.Task)
                {
                    return false;
                }
            }
            finally { core.NavigationCompleted -= OnLoaded; }
            if (generation != _readyGeneration || !ReadyTabWanted
                || _readyTabs.Count >= ReadyTabCapacity || tab.Notice is not null)
            {
                return false;
            }
            tab.EnsureUnveiled();
            _readyTabs.Add(tab);
            _readyTabBuilt[tab] = DateTime.UtcNow;
            kept = true;
            double ms = Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, 1);
            DiagPerf("ready-tab", new { ms, count = _readyTabs.Count });
            Quik.Test("ready tab made in " + ms + " ms; pool " + _readyTabs.Count);
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "quik: make a ready tab");
            return false;
        }
        finally
        {
            _warmingReadyTab = null;
            if (!kept) { DisposeReadyTab(tab); }
        }
    }

    private bool TakeReadyTab(long requested)
    {
        if (!ReadyTabWanted) { return false; }
        RefreshReadyTabsIfStale();
        BrowserTab? tab = _readyTabs.FirstOrDefault(t => t.IsReady && !t.IsLoading && t.Notice is null);
        if (tab is null) { return false; }
        _readyTabs.Remove(tab);
        _readyTabBuilt.Remove(tab);
        int at = NewTabSlot();
        _tabs.Insert(at, tab);
        SelectTab(tab);
        RebuildTabStrip();
        UpdateChrome();
        FocusNewTab(tab, plainNewTab: true);   // only a plain, requested new tab ever comes from the pool
        double ms = Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(requested).TotalMilliseconds, 2);
        DiagPerf("tab-open", new { tab = tab.Id, ms, ready = true, tabs = _tabs.Count });
        DiagUi("tab-open", new { tab = tab.Id, reason = "new tab (ready)", index = at, tabs = _tabs.Count });
        Quik.Test("tab-open " + ms + " ms (ready)");
        QueueReadyTab();
        return true;
    }

    private IEnumerable<BrowserTab> TabsAndReady()
    {
        foreach (BrowserTab tab in _tabs) { yield return tab; }
        foreach (BrowserTab tab in _readyTabs) { yield return tab; }
        if (_warmingReadyTab is { } warming) { yield return warming; }
    }

    private void RefreshReadyTabsIfStale()
    {
        foreach (BrowserTab tab in _readyTabs.ToArray())
        {
            if (_readyTabBuilt.TryGetValue(tab, out DateTime built) && DateTime.UtcNow - built > ReadyTabFreshFor)
            {
                _readyTabBuilt[tab] = DateTime.UtcNow;
                tab.NavigateToHtml(BuildStartPageHtml(), "Start Page");
            }
        }
    }

    private void ReconcileReadyTabs()
    {
        if (!ReadyTabWanted) { DropReadyTab(); return; }
        while (_readyTabs.Count > ReadyTabCapacity)
        {
            BrowserTab tab = _readyTabs[^1];
            _readyTabs.RemoveAt(_readyTabs.Count - 1);
            _readyTabBuilt.Remove(tab);
            DisposeReadyTab(tab);
        }
        QueueReadyTab();
    }

    private void DropReadyTab()
    {
        _readyGeneration++;
        _readyTabTimer?.Stop();
        foreach (BrowserTab tab in _readyTabs.ToArray()) { DisposeReadyTab(tab); }
        _readyTabs.Clear();
        _readyTabBuilt.Clear();
        // The asynchronous builder owns its tab until it can safely dispose it.
    }

    private void DisposeReadyTab(BrowserTab tab)
    {
        try { ContentHost.Children.Remove(tab.View); tab.Dispose(); }
        catch (Exception ex) { Diagnostics.Swallowed(ex, "quik: put a ready tab away"); }
    }
}