using System;
using System.Collections.Generic;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Aviary;

/// <summary>
/// Resolving colours against the theme Aviary is ACTUALLY SHOWING.
///
/// Aviary pins its theme on RootGrid (Settings → Theme → Light / Dark). Anything inside
/// RootGrid's element tree follows that through ThemeResource. Anything OUTSIDE it follows
/// Windows instead — and there turned out to be a lot outside it:
///
///   - Application.Current.Resources[key] resolves a theme dictionary against the
///     APPLICATION's theme, which is Windows'. Every code-side brush used that, so on a
///     dark-Windows machine pinned to Light, the sidebar's section headers and segment
///     glyphs came out white-on-light and vanished.
///   - The Start Page's whole palette is generated from those brushes, so it rendered DARK
///     inside light chrome.
///   - The WebView's default background came from the same lookup, so navigations flashed
///     the wrong colour.
///   - Flyouts and popups render in the XamlRoot's popup layer, not under RootGrid.
///   - The system caption buttons follow Windows unless told otherwise.
///
/// All of it was invisible whenever the pinned theme happened to match Windows — which on
/// this machine is always, unless you pin Light. Found in Phase 5 M0 by switching the theme
/// in the running app rather than trusting that it compiled.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>"Light", "Dark" or "HighContrast" — the dictionary key for what is on screen.</summary>
    private string EffectiveThemeKey()
    {
        try
        {
            // A Windows contrast theme overrides any pin; that is the entire point of it.
            if (new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast)
            {
                return "HighContrast";
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "theme: high-contrast probe");
        }

        return RootGrid.ActualTheme == ElementTheme.Light ? "Light" : "Dark";
    }

    /// <summary>
    /// A brush from the theme dictionary Aviary is actually showing.
    ///
    /// Walks the merged dictionaries newest-first — the look file (Classic or Liquid Glass)
    /// is always the last one merged — and reads the named theme's sub-dictionary directly,
    /// instead of letting the resource system pick one by the Windows theme.
    /// </summary>
    private Brush ThemeBrush(string key)
    {
        string theme = EffectiveThemeKey();

        try
        {
            var merged = Application.Current.Resources.MergedDictionaries;
            for (int i = merged.Count - 1; i >= 0; i--)
            {
                if (merged[i].ThemeDictionaries.TryGetValue(theme, out object? found)
                    && found is ResourceDictionary dictionary
                    && dictionary.TryGetValue(key, out object? value)
                    && value is Brush brush)
                {
                    return brush;
                }
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "theme: brush lookup " + key);
        }

        // Not in any theme dictionary: fall back to the ordinary lookup, and failing that
        // to transparent, which is invisible rather than wrong.
        return Res<Brush>(key, new SolidColorBrush(Microsoft.UI.Colors.Transparent));
    }

    private static Windows.UI.Color ColorOf(Brush brush) =>
        brush is SolidColorBrush solid ? solid.Color : Microsoft.UI.Colors.Transparent;

    /// <summary>
    /// Makes a menu follow Aviary's theme. Menus render in the popup layer, outside
    /// RootGrid, so without this a menu opened while pinned Light is drawn Dark.
    ///
    /// NOT via MenuFlyoutPresenterStyle with a RequestedTheme setter — that was the first
    /// attempt, it compiles, and it does nothing: verified in the running app on 10 Sep, the
    /// tab menu still came up dark inside light chrome. What does work is setting
    /// RequestedTheme on the LIVE popup element once it exists, which forces every
    /// ThemeResource beneath it to re-resolve — the same mechanism ApplyLook's theme nudge
    /// already relies on.
    ///
    /// Submenus open their own popups later, with no event of their own, so the items
    /// inside each submenu re-run the pass when they load.
    /// </summary>
    private MenuFlyout Themed(MenuFlyout menu, string name = "menu")
    {
        menu.Opened += (_, _) =>
        {
            ThemeOpenPopups();
            HookSubmenus(menu.Items);

            // Every Aviary menu passes through here, which makes it the one place that can
            // record, for diagnostics, that a menu opened and which item was chosen.
            DiagUi("menu-open", new { menu = name });
            TrackMenuItems(menu.Items, name);
        };

        return menu;
    }

    /// <summary>Items already carrying a diagnostics click handler — never two on one item.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MenuFlyoutItemBase, string> TrackedItems = new();

    private void TrackMenuItems(IList<MenuFlyoutItemBase> items, string menu)
    {
        foreach (MenuFlyoutItemBase item in items)
        {
            if (item is MenuFlyoutSubItem sub)
            {
                TrackMenuItems(sub.Items, menu);
                continue;
            }

            if (item is MenuFlyoutItem entry && !TrackedItems.TryGetValue(entry, out _))
            {
                TrackedItems.Add(entry, menu);
                entry.Click += (_, _) => DiagUi("menu", new { menu, item = entry.Text });
            }
        }
    }

    private void HookSubmenus(IList<MenuFlyoutItemBase> items)
    {
        foreach (MenuFlyoutItemBase item in items)
        {
            if (item is not MenuFlyoutSubItem sub)
            {
                continue;
            }

            foreach (MenuFlyoutItemBase child in sub.Items)
            {
                child.Loaded -= OnSubmenuItemLoaded;
                child.Loaded += OnSubmenuItemLoaded;
            }

            HookSubmenus(sub.Items);
        }
    }

    private void OnSubmenuItemLoaded(object sender, RoutedEventArgs e) => ThemeOpenPopups();

    /// <summary>Hands the shown theme to every popup currently open in this window.</summary>
    private void ThemeOpenPopups()
    {
        try
        {
            if (RootGrid.XamlRoot is null)
            {
                return;
            }

            foreach (Popup popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(RootGrid.XamlRoot))
            {
                if (popup.Child is FrameworkElement child && child.RequestedTheme != RootGrid.ActualTheme)
                {
                    child.RequestedTheme = RootGrid.ActualTheme;
                }
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "theme: open popups");
        }
    }

    /// <summary>
    /// The system caption buttons follow Windows unless their colours are set, so pinning
    /// Light on a dark machine left white minimise/maximise glyphs on a light toolbar —
    /// all but invisible. Coloured from the same tokens as every other toolbar glyph. The
    /// close button keeps Windows' own red on hover.
    /// </summary>
    private void ApplyCaptionColors()
    {
        try
        {
            if (_appWindow is null || !AppWindowTitleBar.IsCustomizationSupported())
            {
                return;
            }

            AppWindowTitleBar bar = _appWindow.TitleBar;
            Windows.UI.Color glyph = ColorOf(ThemeBrush("AvIconBrush"));
            Windows.UI.Color quiet = ColorOf(ThemeBrush("AvLabelTertiaryBrush"));

            bar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            bar.ButtonForegroundColor = glyph;
            bar.ButtonHoverForegroundColor = glyph;
            bar.ButtonPressedForegroundColor = glyph;
            bar.ButtonInactiveForegroundColor = quiet;
            bar.ButtonHoverBackgroundColor = ColorOf(ThemeBrush("AvHoverBrush"));
            bar.ButtonPressedBackgroundColor = ColorOf(ThemeBrush("AvPressBrush"));
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "theme: caption colours");
        }
    }

    /// <summary>
    /// Everything that holds a colour resolved in code has to be re-resolved when the theme
    /// changes, because a code-assigned brush is a snapshot and never follows on its own.
    /// </summary>
    private void OnEffectiveThemeChanged()
    {
        ApplyWebViewBackground();
        ApplyCaptionColors();
        RestampSegments();
        RefreshSidebar();

        // The Smart Search popup caches its shell brushes; rebuild it next time it opens.
        _suggestPopup = null;

        // Its palette is baked into the HTML when it is built, so it has to be rebuilt.
        RefreshStartPages();
    }
}
