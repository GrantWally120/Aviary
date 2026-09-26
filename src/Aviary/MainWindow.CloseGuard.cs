using System;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;

namespace Aviary;

/// <summary>
/// Two small tab choices from Alpha Build 5 M4.5, both off the Content page in Settings:
///
/// <list type="bullet">
/// <item><b>Ask before closing a window with several tabs</b> (<c>warnManyTabs</c>) — the
/// window's own close is cancelled once, the question is asked, and only an answer of yes
/// closes it. Safari asks the same question, and for the same reason: one stray click on the
/// × should not take six pages with it.</item>
/// <item><b>When the last tab closes</b> (<c>lastTabCloses</c>) — close the window, as every
/// browser on this platform does and as Aviary always has, or keep the window and show the
/// Start Page, which is what Firefox and Edge can do.</item>
/// </list>
///
/// A private window never asks: its tabs are meant to be thrown away, and the question would
/// be a prompt to think again about a window whose whole point is that it leaves nothing.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The question has been asked and answered; the next close goes straight through.</summary>
    private bool _closeConfirmed;

    private void WatchForManyTabsClose()
    {
        if (_appWindow is not null)
        {
            _appWindow.Closing += OnAppWindowClosing;
        }
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        try
        {
            // Cancel THIS close and do what comes first; a dialog cannot be awaited from inside the
            // event, and a window that closed while the question was on screen would answer it itself.
            if (BeforeClose() is { } first)
            {
                args.Cancel = true;
                _ = first();
            }
        }
        catch (Exception ex)
        {
            // Never let this stop a window from closing: the fallback is the old behaviour.
            Diagnostics.Swallowed(ex, "close: several tabs question");
        }
    }

    /// <summary>
    /// A close Aviary makes itself - the menu's Exit, the last tab closing. Window.Close() does NOT
    /// pass through AppWindow.Closing (only the title bar's ×, Alt+F4 and the taskbar do), so on its
    /// own it skipped both questions and the forgetting (bug 96, 26 Sep: Exit closed a window of two
    /// tabs without asking, and "Forget everything when Aviary closes" forgot nothing).
    /// </summary>
    private void RequestClose()
    {
        try
        {
            if (BeforeClose() is { } first)
            {
                _ = first();
                return;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "close: before closing");
        }

        Close();
    }

    /// <summary>
    /// What has to happen before this window may close, or null for nothing: the downloads question,
    /// the several-tabs question, then "Forget everything when Aviary closes" - each at most once.
    /// </summary>
    private Func<Task>? BeforeClose()
    {
        // M5: closing the last window closes Aviary, and that stops every download still
        // running - Safari asks first, and so does this, whatever the tabs setting says.
        if (!_closeConfirmed && RunningDownloadsHere() > 0)
        {
            return AskBeforeStoppingDownloadsAsync;
        }

        if (!_closeConfirmed && !_private && _tabs.Count >= 2 && AppSettings.GetBool("warnManyTabs"))
        {
            return AskBeforeClosingAsync;
        }

        // M6 stage 4: the close is really going ahead - held once more when it is the last ordinary
        // window and there is something to forget (MainWindow.ForgetEverything.cs).
        if (ForgetEverythingDue())
        {
            return CloseForRealAsync;
        }

        return null;
    }

    /// <summary>Every question is answered: forget, if this close owes it, then close for real.</summary>
    private async Task CloseForRealAsync()
    {
        _closeConfirmed = true;
        try
        {
            await ForgetEverythingIfDueAsync();
        }
        catch (Exception ex)
        {
            // Whatever happened, the close goes through; an unfinished forget is owed to the next launch.
            Diagnostics.Swallowed(ex, "close: forget everything");
        }

        Close();
    }

    private async Task AskBeforeStoppingDownloadsAsync()
    {
        try
        {
            int running = RunningDownloadsHere();
            string closing = OpenWindows.Count == 1 ? "Closing Aviary" : "Closing this window";
            DiagUi("close-asked", new { downloads = running });

            bool close = await ConfirmAsync(
                running == 1 ? "A download is still in progress" : running + " downloads are still in progress",
                closing + " stops " + (running == 1 ? "it" : "them") + ". The list keeps "
                    + (running == 1 ? "it" : "them") + ", with a button to download " + (running == 1 ? "it" : "them") + " again.",
                "Close and Stop");

            if (!close)
            {
                DiagUi("close-cancelled", new { downloads = running });
                return;
            }

            await CloseForRealAsync();
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "close: downloads question");
        }
    }

    private async Task AskBeforeClosingAsync()
    {
        try
        {
            int count = _tabs.Count;
            DiagUi("close-asked", new { tabs = count });

            bool close = await ConfirmAsync(
                "Close this window?",
                count + " tabs are open. Closing the window closes all of them. Your history and "
                      + "favourites are kept; anything you have typed into a page is not.",
                "Close " + count + " Tabs");

            if (!close)
            {
                DiagUi("close-cancelled", new { tabs = count });
                return;
            }

            await CloseForRealAsync();
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "close: several tabs answer");
        }
    }

    /// <summary>
    /// The icon on the window, in the taskbar and in Alt-Tab (M4.6). Two are shipped - the blue
    /// tile and the glass one - and Settings -> Accessibility -> App Icon chooses between them
    /// while Aviary runs. The icon INSIDE Aviary.exe cannot change while it runs, which is why
    /// the row says Explorer keeps showing the built-in one.
    /// </summary>
    private void ApplyAppIcon()
    {
        try
        {
            if (_appWindow is null)
            {
                return;
            }

            bool glass = AppSettings.Get("appIcon").StartsWith("Glass", StringComparison.OrdinalIgnoreCase);
            string file = glass ? "Aviary-Glass.ico" : "Aviary.ico";
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", file);

            if (System.IO.File.Exists(path))
            {
                _appWindow.SetIcon(path);
            }
        }
        catch (Exception ex)
        {
            // An icon that will not load leaves the one the exe carries, which is the same mark.
            Diagnostics.Swallowed(ex, "window: app icon");
        }
    }

    /// <summary>
    /// "Show keyboard focus clearly" for Aviary's own interface (M4.5). WinUI draws a focus
    /// visual around whatever the keyboard is on: a white ring with a thin dark one inside it,
    /// the same in every app. This makes it thicker and paints it in the accent colour.
    ///
    /// These are PROPERTIES on every FrameworkElement, not only theme resources, and each
    /// control resolves its own when its template is applied — so putting the keys in a
    /// dictionary after the chrome was built changed nothing (checked with a screenshot: the
    /// ring stayed white). Both halves are needed: the app-level resources, which every control
    /// built from here on reads, and a walk over the controls that already exist.
    /// </summary>
    private void ApplyFocusVisuals()
    {
        try
        {
            bool clear = AppSettings.GetBool("focusRing");
            Microsoft.UI.Xaml.ResourceDictionary app = Microsoft.UI.Xaml.Application.Current.Resources;

            if (clear)
            {
                app["FocusVisualPrimaryThickness"] = new Microsoft.UI.Xaml.Thickness(3);
                app["FocusVisualSecondaryThickness"] = new Microsoft.UI.Xaml.Thickness(1);
                app["FocusVisualPrimaryBrush"] = ThemeBrush("AvAccentBrush");
                app["FocusVisualSecondaryBrush"] = ThemeBrush("AvPageBrush");
            }
            else
            {
                // Off: the keys go, and WinUI falls back to its own — not to a copy of them kept
                // here, which would drift the first time the framework changed its own default.
                foreach (string key in new[]
                         {
                             "FocusVisualPrimaryThickness", "FocusVisualSecondaryThickness",
                             "FocusVisualPrimaryBrush", "FocusVisualSecondaryBrush",
                         })
                {
                    app.Remove(key);
                }
            }

            StampFocusVisuals(RootGrid, clear);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "accessibility: focus visuals");
        }
    }

    /// <summary>
    /// The controls already on screen, which resolved their focus visual before the switch moved.
    /// Setting the property locally beats the template's value; clearing it puts the element back
    /// to whatever the resources now say.
    /// </summary>
    private void StampFocusVisuals(Microsoft.UI.Xaml.DependencyObject root, bool clear)
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            Microsoft.UI.Xaml.DependencyObject child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is Microsoft.UI.Xaml.FrameworkElement element)
            {
                if (clear)
                {
                    element.FocusVisualPrimaryThickness = new Microsoft.UI.Xaml.Thickness(3);
                    element.FocusVisualSecondaryThickness = new Microsoft.UI.Xaml.Thickness(1);
                    element.FocusVisualPrimaryBrush = ThemeBrush("AvAccentBrush");
                    element.FocusVisualSecondaryBrush = ThemeBrush("AvPageBrush");
                }
                else
                {
                    element.ClearValue(Microsoft.UI.Xaml.FrameworkElement.FocusVisualPrimaryThicknessProperty);
                    element.ClearValue(Microsoft.UI.Xaml.FrameworkElement.FocusVisualSecondaryThicknessProperty);
                    element.ClearValue(Microsoft.UI.Xaml.FrameworkElement.FocusVisualPrimaryBrushProperty);
                    element.ClearValue(Microsoft.UI.Xaml.FrameworkElement.FocusVisualSecondaryBrushProperty);
                }
            }

            StampFocusVisuals(child, clear);
        }
    }

    /// <summary>Tells Aviary's own pages the focus ring changed, the way motion is told.</summary>
    private void PushFocusRingToPages()
    {
        bool clear = AppSettings.GetBool("focusRing");
        string script = clear
            ? "document.documentElement.dataset.focus='clear'"
            : "delete document.documentElement.dataset.focus";

        foreach (BrowserTab tab in _tabs)
        {
            if (!tab.IsInternalPage || tab.View.CoreWebView2 is not Microsoft.Web.WebView2.Core.CoreWebView2 core)
            {
                continue;
            }

            try
            {
                _ = core.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                Diagnostics.Swallowed(ex, "accessibility: tell page about the focus ring");
            }
        }
    }

    /// <summary>
    /// The last tab in the window has been closed. True when the window should stay open with a
    /// fresh Start Page instead — the person's choice, and never in a private window, where an
    /// empty window left behind would look like the private session is still going.
    /// </summary>
    private bool KeepWindowAfterLastTab()
        => !_private
        && string.Equals(AppSettings.Get("lastTabCloses"), "Show the Start Page", StringComparison.OrdinalIgnoreCase);
}
