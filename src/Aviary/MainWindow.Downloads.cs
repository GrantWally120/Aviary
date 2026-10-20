using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Web.WebView2.Core;
using Windows.Storage.Pickers;

namespace Aviary;

/// <summary>
/// Downloads, as Safari does them (Alpha Build 5 M5, from the recovered Phase 4 spec): a ring
/// on the toolbar, a popover with a row per file, and a list that is still there tomorrow.
///
/// <para><b>The popover.</b> A row is the file's own Windows icon, its name, and one line that
/// says how far along it is ("3.4 MB of 15.8 MB — About 1 minute remaining"), what it came to
/// ("15.8 MB"), or why it stopped. The control at the end is Stop while it runs, Resume once it
/// has stopped, and the magnifier — Show in Folder — once it has landed, faded in on hover the
/// way the tab strip's close button is. <b>Double-click opens; a single click does nothing</b>,
/// because a list that opens files on one click is a list people open things from by mistake.
/// Right-click: Open · Show in Folder · Copy Address · Remove From List — Safari's menu, word for
/// word, with Finder swapped for Folder.</para>
///
/// <para><b>Programs ask first.</b> Windows has no Gatekeeper, so opening an .exe, .msi, script
/// or disk image from the list asks — "Ask before opening a downloaded program", on by default.
/// That row existed only as a Safety Check line until now (bug 88): the check reported a
/// protection nothing provided.</para>
/// </summary>
public sealed partial class MainWindow
{
    private DownloadRing? _ring;
    private Flyout? _downloadsFlyout;
    private StackPanel? _downloadsList;
    private readonly Dictionary<string, DownloadRow> _downloadRows = new();
    private DateTimeOffset _downloadsAutoOpened = DateTimeOffset.MinValue;
    private long _downloadsClosedAt;
    private bool _downloadsQueued;
    private bool _progressQueued;

    /// <summary>
    /// AVIARY_OPEN_TEST=1 — Open records what it WOULD open instead of launching it, so the tests
    /// can prove the question is asked without running a program. Environment only, like
    /// AVIARY_CRASH_TEST: nothing inside the browser can set it.
    /// </summary>
    private static readonly bool OpenTest = Environment.GetEnvironmentVariable("AVIARY_OPEN_TEST") == "1";

    private sealed class DownloadRow
    {
        public required DownloadRecord Record { get; init; }
        public required TextBlock Name { get; init; }
        public required TextBlock Status { get; init; }
        public required Button Action { get; init; }
        public long Updated { get; set; }
    }

    private void WireDownloads()
    {
        _ring = new DownloadRing(DownloadsButton, RingTrack, RingProgress, RingSpin, DispatcherQueue, _private);
        Downloads.Changed += OnDownloadsChanged;
        Downloads.Progressed += OnDownloadProgressed;
        UpdateDownloadsButton(arriving: false);
    }

    private void UnwireDownloads()
    {
        Downloads.Changed -= OnDownloadsChanged;
        Downloads.Progressed -= OnDownloadProgressed;
        _ring?.Stop();
    }

    // ── a download starts ─────────────────────────────────────────────────────

    private void OnDownloadStarted(BrowserTab tab, CoreWebView2DownloadOperation operation)
    {
        Downloads.Start(operation, _private, tab.Id);
        UpdateDownloadsButton(arriving: true);

        // A tab opened only to fetch this (target="_blank", or "Download again") has nothing to
        // show now that it turned out to be a download: it goes, as it does in Chrome. Never the
        // window's only tab. Enqueued, because the engine is still inside its own event.
        if (tab.AwaitingFirstPage)
        {
            tab.AwaitingFirstPage = false;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_tabs.Contains(tab) && _tabs.Count > 1)
                {
                    DiagUi("download-tab-closed");
                    CloseTab(tab);
                }
                else if (_tabs.Contains(tab) && tab.View.CoreWebView2 is CoreWebView2 core)
                {
                    core.WebMessageReceived += StartPage_WebMessageReceived;
                    tab.NavigateToHtml(BuildStartPageHtml(), "Start Page");
                }
            });
        }

        // Safari opens the list when a download starts — but never more than once every four
        // seconds (a page firing five downloads must not strobe it), never over a menu that is
        // already open, and never for a window that is not the one in front.
        if (DateTimeOffset.UtcNow - _downloadsAutoOpened > TimeSpan.FromSeconds(4)
            && _downloadsFlyout is null && IsForeground() && !AnyPopupOpen())
        {
            _downloadsAutoOpened = DateTimeOffset.UtcNow;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_downloadsFlyout is null && DownloadsButton.Visibility == Visibility.Visible)
                {
                    ShowDownloads(DownloadsButton, auto: true);
                }
            });
        }
    }

    private bool IsForeground()
    {
        try
        {
            return GetForegroundWindow() == WinRT.Interop.WindowNative.GetWindowHandle(this);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool AnyPopupOpen()
    {
        try
        {
            return RootGrid.XamlRoot is not null
                   && VisualTreeHelper.GetOpenPopupsForXamlRoot(RootGrid.XamlRoot).Count > 0;
        }
        catch (Exception)
        {
            return true;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    // ── the list changed ──────────────────────────────────────────────────────

    private void OnDownloadsChanged()
    {
        if (_downloadsQueued)
        {
            return;
        }

        _downloadsQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _downloadsQueued = false;
            ReleaseParkedTabs();
            _ring?.Sync();
            UpdateDownloadsButton(arriving: false);
            if (_downloadsFlyout is not null && _downloadsList is not null)
            {
                FillDownloadsList(_downloadsList);
            }
        });
    }

    /// <summary>Bytes arrived. The ring reads them on its own tick; an open row's line is refreshed a few times a second.</summary>
    private void OnDownloadProgressed(DownloadRecord record)
    {
        if (record.Private != _private || _progressQueued)
        {
            return;
        }

        _progressQueued = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _progressQueued = false;
            _ring?.Sync();
            ToolTipService.SetToolTip(DownloadsButton, _ring?.Tooltip ?? "Downloads");

            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            foreach (DownloadRow row in _downloadRows.Values)
            {
                if (row.Record.Running
                    && System.Diagnostics.Stopwatch.GetElapsedTime(row.Updated, now).TotalMilliseconds > 250)
                {
                    row.Updated = now;
                    row.Status.Text = Downloads.Status(row.Record, Downloads.LiveFor(row.Record));
                }
            }
        });
    }

    /// <summary>Shown while the list has anything in it, as Safari's is — and it arrives with a bump.</summary>
    private void UpdateDownloadsButton(bool arriving)
    {
        bool show = Downloads.Any(_private);
        bool was = DownloadsButton.Visibility == Visibility.Visible;
        DownloadsButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(DownloadsButton, _ring?.Tooltip ?? "Downloads");

        if (show && !was && arriving)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => Motion.Pop(DownloadsButton, 1.18f));
        }
    }

    // ── the popover ───────────────────────────────────────────────────────────

    private void DownloadsButton_Click(object sender, RoutedEventArgs e)
    {
        // The press that dismissed the popover also clicks the button: that press means "close",
        // not "close and open again".
        if (_downloadsClosedAt != 0
            && System.Diagnostics.Stopwatch.GetElapsedTime(_downloadsClosedAt).TotalMilliseconds < 300)
        {
            return;
        }

        ShowDownloads(DownloadsButton, auto: false);
    }

    /// <summary>Ctrl+J, Ctrl+Alt+L and the app menu. With nothing in the list the button is hidden, so the popover hangs from the menu button.</summary>
    private void OpenDownloads()
    {
        if (_downloadsFlyout is not null)
        {
            _downloadsFlyout.Hide();
            return;
        }

        ShowDownloads(DownloadsButton.Visibility == Visibility.Visible ? DownloadsButton : MenuButton, auto: false);
    }

    private void ShowDownloads(FrameworkElement anchor, bool auto)
    {
        if (_downloadsFlyout is not null)
        {
            _downloadsFlyout.Hide();
            return;
        }

        Flyout flyout = new()
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
            Content = BuildDownloadsPanel(),
        };

        flyout.Opened += (_, _) =>
        {
            ThemeOpenPopups();
            DiagUi("menu-open", new { menu = "downloads", rows = _downloadRows.Count, auto });

            // A flyout focuses its first focusable thing, which would be Clear - drawn with a
            // focus ring, as if it were about to be pressed. The newest download takes it instead,
            // programmatically, so there is no ring until the keyboard is used.
            if (_downloadsList?.Children.Count > 0 && _downloadsList.Children[0] is Control first)
            {
                first.Focus(FocusState.Programmatic);
            }
        };
        flyout.Closed += (_, _) =>
        {
            if (ReferenceEquals(_downloadsFlyout, flyout))
            {
                _downloadsFlyout = null;
                _downloadsList = null;
                _downloadRows.Clear();
                _downloadsClosedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            }
        };

        _downloadsFlyout = flyout;
        flyout.ShowAt(anchor);
    }

    private FrameworkElement BuildDownloadsPanel()
    {
        Brush label = ThemeBrush("AvLabelBrush");
        Brush accent = ThemeBrush("AvAccentBrush");
        Brush hairline = ThemeBrush("AvHairlineBrush");

        StackPanel panel = new() { Width = 340, Spacing = 8, RequestedTheme = RootGrid.ActualTheme };

        Grid head = new();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock
        {
            Text = _private ? "Downloads — this private window" : "Downloads",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = label,
            VerticalAlignment = VerticalAlignment.Center,
        });

        HyperlinkButton clear = new() { Content = "Clear", FontSize = 12, Foreground = accent, Padding = new Thickness(6, 2, 6, 2) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(clear, "Clear downloads");
        clear.Click += (_, _) =>
        {
            int removed = Downloads.Clear(_private);
            DiagFeature("downloads-clear", new { removed, from = "popover" });
        };
        Grid.SetColumn(clear, 1);
        head.Children.Add(clear);
        panel.Children.Add(head);
        panel.Children.Add(new Border { Height = 1, Background = hairline });

        StackPanel list = new() { Spacing = 2 };
        _downloadsList = list;
        FillDownloadsList(list);
        panel.Children.Add(new ScrollViewer
        {
            Content = list,
            MaxHeight = 5.5 * 60,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });

        panel.Children.Add(new Border { Height = 1, Background = hairline });
        HyperlinkButton folder = new() { Content = "Show Downloads Folder", FontSize = 12, Foreground = accent, Margin = new Thickness(-10, -4, 0, -6) };
        folder.Click += (_, _) =>
        {
            _downloadsFlyout?.Hide();
            DiagUi("downloads-folder");
            Downloads.RevealFolder();
        };
        panel.Children.Add(folder);
        return panel;
    }

    /// <summary>
    /// The rows, rebuilt when the list's SHAPE changes (a download starting, finishing, removed).
    /// Progress does not come through here: it rewrites one line of one row in place, because a
    /// list rebuilt on every byte loses its hover and flickers.
    /// </summary>
    private void FillDownloadsList(StackPanel list)
    {
        list.Children.Clear();
        _downloadRows.Clear();

        List<DownloadRecord> records = Downloads.List(_private);
        if (records.Count == 0)
        {
            // The button hides when the list is empty, so this is only reachable from Ctrl+J or the
            // menu - and an empty panel there would look broken.
            list.Children.Add(new TextBlock
            {
                Text = "No downloads",
                FontSize = 13,
                Foreground = ThemeBrush("AvLabelSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 28, 0, 8),
            });
            list.Children.Add(new TextBlock
            {
                Text = "Files you download will appear here.",
                FontSize = 12,
                Foreground = ThemeBrush("AvLabelSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(12, 0, 12, 28),
            });
            return;
        }

        foreach (DownloadRecord record in records)
        {
            list.Children.Add(BuildDownloadRow(record));
        }
    }

    private FrameworkElement BuildDownloadRow(DownloadRecord record)
    {
        Brush label = ThemeBrush("AvLabelBrush");
        Brush secondary = ThemeBrush("AvLabelSecondaryBrush");
        Brush tertiary = ThemeBrush("AvLabelTertiaryBrush");
        Brush hover = ThemeBrush("AvHoverBrush");
        DownloadItem? live = Downloads.LiveFor(record);

        Grid grid = new() { Height = 52, ColumnSpacing = 10, Padding = new Thickness(6, 0, 4, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });

        FrameworkElement icon = FileIcons.For(record.Extension) is ImageSource source
            ? new Image { Source = source, Width = 32, Height = 32, Opacity = record.Gone ? 0.45 : 1 }
            : new Border { Width = 26, Height = 32, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1.2), BorderBrush = secondary };
        icon.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(icon);

        StackPanel text = new() { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        TextBlock name = new()
        {
            Text = record.FileName.Length > 0 ? record.FileName : "Download",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = record.Gone ? tertiary : label,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        TextBlock status = new()
        {
            Text = Downloads.Status(record, live),
            FontSize = 12,
            Foreground = record.Gone ? tertiary : secondary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        text.Children.Add(name);
        text.Children.Add(status);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        // The one control at the end: Stop while it runs, Resume once stopped, Show in Folder when done.
        Button action = new()
        {
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        bool reveal = false;
        if (record.Running)
        {
            action.Content = StopGlyph(secondary);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(action, "Stop " + record.FileName);
            ToolTipService.SetToolTip(action, "Stop");
            action.Click += (_, _) =>
            {
                DiagFeature("download-stop");
                Downloads.LiveFor(record)?.Cancel();
            };
        }
        else if (record.Interrupted)
        {
            action.Content = ResumeGlyph(secondary);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(action, "Resume " + record.FileName);
            ToolTipService.SetToolTip(action, live?.CanResume == true ? "Resume" : "Download again");
            action.Click += (_, _) => _ = ResumeDownloadAsync(record);
        }
        else if (!record.Gone)
        {
            reveal = true;
            action.Content = MagnifierGlyph(secondary);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(action, "Show " + record.FileName + " in Folder");
            ToolTipService.SetToolTip(action, "Show in Folder");
            // Hidden by the composition layer, not by Opacity: FadeTo animates that layer, and
            // the two multiply - an element at Opacity 0 would stay invisible however it faded.
            Motion.VisualOf(action).Opacity = 0f;
            action.Click += (_, _) =>
            {
                _downloadsFlyout?.Hide();
                DiagFeature("download-reveal", new { from = "row" });
                Downloads.Reveal(record);
            };
        }
        else
        {
            // Moved or deleted outside Aviary: a reveal button here would be present and broken.
            action.Visibility = Visibility.Collapsed;
        }

        Grid.SetColumn(action, 2);
        grid.Children.Add(action);

        Border back = new() { CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Child = grid };
        DownloadRowControl row = new()
        {
            Content = back,
            IsTabStop = true,
            UseSystemFocusVisuals = true,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Activate = () => _ = OpenDownloadAsync(record, "row"),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row, record.FileName + ", " + status.Text);

        // The magnifier fades in on hover with the same timing as the tab's favicon-to-close swap.
        row.PointerEntered += (_, _) =>
        {
            back.Background = hover;
            if (reveal) { Motion.FadeTo(action, 1f, Motion.Quick); }
        };
        row.PointerExited += (_, _) =>
        {
            back.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            if (reveal) { Motion.FadeTo(action, 0f, Motion.Quick); }
        };
        row.GotFocus += (_, _) => { if (reveal) { Motion.FadeTo(action, 1f, Motion.Quick); } };

        row.DoubleTapped += (_, e) =>
        {
            e.Handled = true;
            _ = OpenDownloadAsync(record, "double-click");
        };
        row.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                _ = OpenDownloadAsync(record, "enter");
            }
            else if (e.Key is Windows.System.VirtualKey.Delete or Windows.System.VirtualKey.Back)
            {
                e.Handled = true;
                Downloads.Remove(record);
            }
        };

        // M5 stage 2 - Safari lets a finished download be dragged out of the list into Finder; here,
        // into Explorer or the desktop. The real file goes, as a COPY: the list's own file stays put.
        if (record.Completed && !record.Gone)
        {
            row.CanDrag = true;
            row.DragStarting += async (_, e) =>
            {
                Microsoft.UI.Xaml.DragOperationDeferral wait = e.GetDeferral();
                try
                {
                    Windows.Storage.StorageFile file = await Windows.Storage.StorageFile.GetFileFromPathAsync(record.FilePath);
                    e.Data.SetStorageItems(new[] { file });
                    e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
                    e.AllowedOperations = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
                    DiagFeature("download-drag");
                }
                catch (Exception ex)
                {
                    e.Cancel = true;
                    Diagnostics.Swallowed(ex, "download: drag out");
                }
                finally
                {
                    wait.Complete();
                }
            };
        }

        MenuFlyout menu = Themed(new MenuFlyout(), "download row");
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            AddRowItem(menu, "Open", record.Completed && !record.Gone, () => _ = OpenDownloadAsync(record, "menu"));
            AddRowItem(menu, "Show in Folder", true, () =>
            {
                _downloadsFlyout?.Hide();
                DiagFeature("download-reveal", new { from = "menu" });
                Downloads.Reveal(record);
            });
            AddRowItem(menu, "Copy Address", record.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase), () => CopyText(record.Url));
            menu.Items.Add(new MenuFlyoutSeparator());
            AddRowItem(menu, "Remove From List", true, () =>
            {
                DiagFeature("download-remove");
                Downloads.Remove(record);
            });
        };
        row.ContextFlyout = menu;

        _downloadRows[record.Id] = new DownloadRow { Record = record, Name = name, Status = status, Action = action };
        return row;
    }

    private static void AddRowItem(MenuFlyout menu, string text, bool enabled, Action act)
    {
        MenuFlyoutItem item = new() { Text = text, IsEnabled = enabled };
        item.Click += (_, _) => act();
        menu.Items.Add(item);
    }

    // ── what a row does ──────────────────────────────────────────────────────

    /// <summary>Opens a finished file — asking first when it is a program and the setting says to.</summary>
    private async Task OpenDownloadAsync(DownloadRecord record, string from)
    {
        try
        {
            if (!record.Completed || record.Gone)
            {
                return;
            }

            bool program = Downloads.IsProgram(record);
            bool asked = false;
            if (program && AppSettings.GetBool("warnExecutables"))
            {
                _downloadsFlyout?.Hide();
                string site = BrowserTab.HostOf(record.Url);
                asked = true;
                bool go = await ConfirmAsync(
                    "Open “" + record.FileName + "”?",
                    "“" + record.FileName + "” is a program" + (site.Length > 0 ? ", downloaded from " + site : "")
                        + ". A program can change anything your Windows account can. Open it only if you trust where it came from.",
                    "Open");
                if (!go)
                {
                    DiagFeature("download-open", new { from, program, asked, opened = false });
                    return;
                }
            }

            _downloadsFlyout?.Hide();
            bool opened = OpenTest || Downloads.Open(record);
            DiagFeature("download-open", new { from, program, asked, opened, test = OpenTest ? true : (bool?)null });
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "download: open from the list");
        }
    }

    /// <summary>
    /// Resume where the engine can (the connection dropped and the server allows ranges); otherwise
    /// ask for the file again from where it came from. A download from before Aviary was last closed
    /// always takes the second path — the engine that was fetching it has gone.
    /// </summary>
    private async Task ResumeDownloadAsync(DownloadRecord record)
    {
        try
        {
            if (Downloads.LiveFor(record) is { } live && live.Resume())
            {
                DiagFeature("download-resume", new { how = "resumed" });
                return;
            }

            if (!record.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            DiagFeature("download-resume", new { how = "again" });
            Downloads.Remove(record);
            await FetchInBackgroundTabAsync(record.Url);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "download: resume");
        }
    }

    /// <summary>
    /// Asks for an address in a background tab that shows nothing of its own. If the answer is a
    /// download, the tab closes itself the moment the download starts (see <see cref="OnDownloadStarted"/>);
    /// if it turns out to be a page after all, it stays, as any link would.
    /// </summary>
    private async Task FetchInBackgroundTabAsync(string url)
    {
        BrowserTab? tab = await CreateTabAsync(select: false, reason: "download again");
        if (tab is null)
        {
            return;
        }

        tab.AwaitingFirstPage = true;
        tab.Navigate(url);
    }

    // ── tabs closed while they download ───────────────────────────────────────

    /// <summary>Closed tabs whose engines are kept, out of sight, until their downloads end.</summary>
    private readonly List<BrowserTab> _parked = new();

    /// <summary>
    /// Every closed tab goes through here. In WebView2 a download dies with the engine of the tab
    /// that started it, so a tab that is still fetching one is PARKED - gone from the strip, muted,
    /// hidden, still in the tree (a WebView taken out of the tree is closed) - and let go when its
    /// last download ends (<see cref="ReleaseParkedTabs"/>).
    /// </summary>
    private void DisposeOrPark(BrowserTab tab)
    {
        if (Downloads.RunningFor(tab.Id) > 0)
        {
            tab.Park();
            _parked.Add(tab);
            DiagUi("tab-parked", new { tab = tab.Id, downloads = Downloads.RunningFor(tab.Id) });
            return;
        }

        ContentHost.Children.Remove(tab.View);
        tab.Dispose();
    }

    private void ReleaseParkedTabs()
    {
        for (int i = _parked.Count - 1; i >= 0; i--)
        {
            BrowserTab tab = _parked[i];
            if (Downloads.RunningFor(tab.Id) == 0)
            {
                _parked.RemoveAt(i);
                ContentHost.Children.Remove(tab.View);
                tab.Dispose();
                DiagUi("tab-unparked", new { tab = tab.Id });
            }
        }
    }

    /// <summary>Downloads still running in this window, parked tabs included - closing the window stops them.</summary>
    private int RunningDownloadsHere()
    {
        int n = 0;
        foreach (BrowserTab tab in _tabs)
        {
            n += Downloads.RunningFor(tab.Id);
        }

        foreach (BrowserTab tab in _parked)
        {
            n += Downloads.RunningFor(tab.Id);
        }

        return n;
    }

    // ── where files go ────────────────────────────────────────────────────────

    /// <summary>
    /// The folder downloads land in, handed to the engine. It lives on the engine's PROFILE, which
    /// the normal windows share and the private ones have their own of, so it is set on every tab's.
    /// </summary>
    private static void ApplyDownloadFolderEverywhere()
    {
        foreach (MainWindow window in OpenWindows)
        {
            foreach (BrowserTab tab in window._tabs)
            {
                tab.ApplyDownloadFolder();
            }
        }
    }

    /// <summary>Settings → Downloads → Location → Change…: Windows' own folder picker.</summary>
    private async Task ChooseDownloadFolderAsync(CoreWebView2 page)
    {
        try
        {
            FolderPicker picker = new() { SuggestedStartLocation = PickerLocationId.Downloads };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

            Windows.Storage.StorageFolder? folder = await picker.PickSingleFolderAsync();
            if (folder is null || string.IsNullOrEmpty(folder.Path) || !Directory.Exists(folder.Path))
            {
                return;
            }

            // The Windows Downloads folder itself is stored as "" - "the Downloads folder", which
            // follows it if it is ever moved - rather than as today's path to it.
            string chosen = string.Equals(folder.Path.TrimEnd('\\'), KnownFolders.Downloads.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : folder.Path;
            AppSettings.Set("downloadDir", chosen);
            ApplyDownloadFolderEverywhere();
            DiagFeature("download-folder", new { standard = chosen.Length == 0 });
            PushDownloadFolderToSettings();
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "downloads: choose a folder");
        }
    }

    /// <summary>One Save window at a time: a page that starts two downloads gets two questions in turn, not two windows.</summary>
    private static readonly System.Threading.SemaphoreSlim SaveAsGate = new(1, 1);

    /// <summary>
    /// Settings → Downloads → "Ask where to save each file" (M5 stage 2): Windows' own Save window,
    /// asked before the download begins. Answers the chosen path, or null for Cancel.
    ///
    /// <para>The WinRT <c>FileSavePicker</c>, not the classic Win32 dialog, on purpose: it runs in
    /// its own process (PickerHost) and is awaited, so Aviary goes on drawing and answering while it
    /// is open. A classic modal dialog runs its own message loop, and a modal loop is exactly what
    /// was found holding Aviary's dispatcher in M4.5 (bug 67). The cost: this picker cannot be told
    /// to OPEN in a given folder - it starts in Downloads and after that wherever you last saved.</para>
    /// </summary>
    private async Task<string?> AskWhereToSaveAsync(string suggestedPath)
    {
        await SaveAsGate.WaitAsync();
        try
        {
            string name = System.IO.Path.GetFileName(suggestedPath);
            string ext = System.IO.Path.GetExtension(name);
            bool plainExt = ext.Length is > 1 and <= 11 && ext.Skip(1).All(char.IsAsciiLetterOrDigit);

            FileSavePicker picker = new()
            {
                SuggestedStartLocation = PickerLocationId.Downloads,
                SuggestedFileName = plainExt ? System.IO.Path.GetFileNameWithoutExtension(name) : (name.Length > 0 ? name : "Download"),
            };

            // FileTypeChoices must not be empty, or the picker throws instead of opening.
            if (plainExt)
            {
                picker.FileTypeChoices.Add(ext.TrimStart('.').ToUpperInvariant() + " file", new List<string> { ext });
            }
            else
            {
                picker.FileTypeChoices.Add("File", new List<string> { "." });
            }

            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

            Windows.Storage.StorageFile? file = await picker.PickSaveFileAsync();

            // What was chosen is not recorded - a path is a piece of the person's disk; only that
            // they were asked, and whether they went ahead.
            DiagFeature("download-save-as", new { saved = file is not null });
            return string.IsNullOrEmpty(file?.Path) ? null : file.Path;
        }
        catch (Exception ex)
        {
            // A picker that could not open must not lose the download: it goes where it would have.
            Diagnostics.Swallowed(ex, "downloads: ask where to save");
            return suggestedPath.Length > 0 ? suggestedPath : null;
        }
        finally
        {
            SaveAsGate.Release();
        }
    }

    /// <summary>The folder downloads land in now, as a person would name it.</summary>
    internal static string DownloadFolder()
    {
        string dir = AppSettings.Get("downloadDir");
        return dir.Length > 0 && Directory.Exists(dir) ? dir : KnownFolders.Downloads;
    }

    private void PushDownloadFolderToSettings()
    {
        string json = System.Text.Json.JsonSerializer.Serialize(DownloadFolder());
        foreach (MainWindow w in OpenWindows)
        {
            foreach (BrowserTab t in w._tabs)
            {
                if (t.IsInternalPage && t.Title == "Settings" && t.View.CoreWebView2 is CoreWebView2 core)
                {
                    _ = core.ExecuteScriptAsync("window.__aviaryDownloadDir && window.__aviaryDownloadDir(" + json + ")");
                }
            }
        }
    }

    // ── glyphs ────────────────────────────────────────────────────────────────

    /// <summary>Safari's stop: a ring with a filled square in it.</summary>
    private static FrameworkElement StopGlyph(Brush brush)
    {
        Grid g = new() { Width = 17, Height = 17 };
        g.Children.Add(new Ellipse { Stroke = brush, StrokeThickness = 1.4 });
        g.Children.Add(new Rectangle { Width = 5.5, Height = 5.5, Fill = brush, RadiusX = 1, RadiusY = 1 });
        return g;
    }

    /// <summary>A circular arrow: resume, or fetch again.</summary>
    private static FrameworkElement ResumeGlyph(Brush brush) => Glyph(brush,
        "M13.6,8.5 A5.1,5.1 0 1 1 11.9,4.7 M12.1,2.2 L12.1,5.1 L9.2,5.1");

    /// <summary>Safari's reveal: a magnifying glass.</summary>
    private static FrameworkElement MagnifierGlyph(Brush brush) => Glyph(brush,
        "M7.3,2.8 A4.5,4.5 0 1 1 7.29,2.8 Z M10.6,10.6 L14.2,14.2");

    private static FrameworkElement Glyph(Brush brush, string data)
    {
        return new Microsoft.UI.Xaml.Shapes.Path
        {
            Width = 17,
            Height = 17,
            Stroke = brush,
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), data),
        };
    }
}

/// <summary>
/// A row in the download list. A control of its own for one reason: a screen reader, and UI
/// Automation generally, can INVOKE it — which opens the file, exactly as double-click and Enter do.
/// Without that, a row that does nothing on a single click would be a row nobody using the
/// keyboard or a screen reader could open.
/// </summary>
internal sealed class DownloadRowControl : ContentControl
{
    public Action? Activate { get; set; }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer : FrameworkElementAutomationPeer, IInvokeProvider
    {
        public Peer(DownloadRowControl owner) : base(owner)
        {
        }

        protected override object GetPatternCore(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Invoke ? this : base.GetPatternCore(patternInterface);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

        protected override string GetClassNameCore() => "DownloadRow";

        public void Invoke() => ((DownloadRowControl)Owner).Activate?.Invoke();
    }
}
