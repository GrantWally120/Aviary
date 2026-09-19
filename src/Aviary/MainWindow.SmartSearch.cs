using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Aviary;

/// <summary>
/// The Smart Search Field: Safari's name for the address bar, and the behaviour that earns
/// it — a list that drops beneath the toolbar as you type, with a Top Hit, matches from
/// your own history and bookmarks, and inline completion of a host you already visit.
///
/// Everything here is answered from local data. Suggestions from the search engine are a
/// separate, off-by-default setting (MainWindow.SearchSuggest.cs, M4), because the owner's
/// stated goal for this browser is "for it to be safe and private" and suggestions-as-you-type
/// means sending what is typed in the address bar to a search engine as it is typed.
/// </summary>
public sealed partial class MainWindow
{
    /// <param name="FromEngine">A search the engine suggested, rather than the words as typed.</param>
    private sealed record Suggestion(string Url, string Title, string Host, bool IsBookmark, bool IsSearch,
        bool FromEngine = false);

    private Popup? _suggestPopup;
    private StackPanel? _suggestList;
    private readonly List<Suggestion> _suggestions = new();
    private int _suggestIndex = -1;
    private bool _suppressAutocomplete;

    /// <summary>
    /// A Popup rather than a Flyout or an in-grid overlay.
    ///
    /// The field lives inside a Border in the toolbar, so an in-grid overlay would be
    /// clipped by its parent; and it has to draw over the WebView, which in WinUI 3
    /// composites into the visual tree rather than sitting in a separate HWND — so a Popup,
    /// which renders in the XamlRoot's own popup layer, is above it either way.
    /// </summary>
    private void EnsureSuggestPopup()
    {
        if (_suggestPopup is not null)
        {
            return;
        }

        _suggestList = new StackPanel { Spacing = 0 };

        Border shell = new()
        {
            Background = ThemeBrush("AvChromeBrush"),
            BorderBrush = ThemeBrush("AvHairlineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(5),
            Shadow = new ThemeShadow(),
            Translation = new System.Numerics.Vector3(0, 0, 32),
            Child = _suggestList,
        };

        // The popup layer sits outside RootGrid, so the pinned theme has to be handed over.
        shell.RequestedTheme = RootGrid.ActualTheme;

        _suggestPopup = new Popup
        {
            Child = shell,
            IsLightDismissEnabled = false,
            XamlRoot = RootGrid.XamlRoot,
        };
    }

    private void ShowSuggestions(string typed)
    {
        EnsureSuggestPopup();

        _suggestions.Clear();
        _suggestIndex = -1;

        string query = (typed ?? string.Empty).Trim();

        if (query.Length == 0)
        {
            HideSuggestions();
            return;
        }

        // The engine's suggestions (M4), from its last answer — empty unless the switch is on.
        // While there are some, your own matches are cut down so the list stays one screen tall.
        bool isSearch = !LooksLikeUrl(query);
        List<string> fromEngine = isSearch ? EngineSuggestionsFor(query) : new List<string>();
        int localLimit = fromEngine.Count > 0 ? LocalLimitWithEngine : int.MaxValue;

        // Top Hit first: bookmarks outrank history, because a bookmark is a place you
        // decided mattered rather than one you merely passed through.
        foreach (Bookmark bookmark in Bookmarks.Items)
        {
            if (bookmark.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || bookmark.Url.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                _suggestions.Add(new Suggestion(bookmark.Url, bookmark.Title,
                    BrowserTab.HostOf(bookmark.Url), IsBookmark: true, IsSearch: false));
            }

            if (_suggestions.Count >= 3)
            {
                break;
            }
        }

        foreach (HistoryEntry entry in History.Search(query, 6))
        {
            if (_suggestions.Count >= localLimit)
            {
                break;
            }

            if (_suggestions.Any(s => string.Equals(s.Url, entry.Url, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _suggestions.Add(new Suggestion(entry.Url, entry.Title, entry.Host,
                IsBookmark: false, IsSearch: false));
        }

        if (isSearch)
        {
            string engine = SearchEngines.Current.Name;

            // What the engine suggests: always a SEARCH for those words, with the same engine —
            // never an address opened, whatever the words look like.
            foreach (string words in fromEngine)
            {
                _suggestions.Add(new Suggestion(SearchPrefix + Uri.EscapeDataString(words),
                    words, engine, IsBookmark: false, IsSearch: true, FromEngine: true));
            }

            // Always last, always present: what typing this and pressing Enter would do.
            _suggestions.Add(new Suggestion(SearchPrefix + Uri.EscapeDataString(query),
                query, engine, IsBookmark: false, IsSearch: true));
        }

        if (_suggestions.Count == 0)
        {
            HideSuggestions();
            return;
        }

        RenderSuggestions();
        PositionSuggestions();
        _suggestPopup!.IsOpen = true;

        // Ask the engine about these words (when the switch is on): its answer redraws this list.
        RequestEngineSuggestions(query);
    }

    private static bool LooksLikeUrl(string text) => SearchSuggestions.LooksLikeUrl(text);

    private void RenderSuggestions()
    {
        if (_suggestList is null)
        {
            return;
        }

        _suggestList.Children.Clear();

        for (int i = 0; i < _suggestions.Count; i++)
        {
            Suggestion s = _suggestions[i];

            Grid content = new() { ColumnSpacing = 9 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            if (s.IsSearch)
            {
                Microsoft.UI.Xaml.Shapes.Path glass = new()
                {
                    Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
                        typeof(Geometry), "M7.2,2.8 A4.4,4.4 0 1 1 7.2,11.6 A4.4,4.4 0 1 1 7.2,2.8 M10.6,10.6 L13.4,13.4"),
                    Width = 16,
                    Height = 16,
                    Stroke = ThemeBrush("AvLabelSecondaryBrush"),
                    StrokeThickness = 1.4,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                };
                Grid.SetColumn(glass, 0);
                content.Children.Add(glass);
            }
            else
            {
                FaviconView icon = new(16, RootGrid.Resources);
                icon.SetSource(s.Url, null, internalPage: false);
                Grid.SetColumn(icon, 0);
                content.Children.Add(icon);
            }

            StackPanel text = new() { Spacing = 0 };
            text.Children.Add(new TextBlock
            {
                Text = s.IsSearch ? s.Title : (string.IsNullOrWhiteSpace(s.Title) ? s.Host : s.Title),
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            });
            text.Children.Add(new TextBlock
            {
                Text = s.FromEngine ? "Suggested by " + s.Host : s.IsSearch ? "Search with " + s.Host : s.Host,
                FontSize = 11.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Foreground = ThemeBrush("AvLabelTertiaryBrush"),
            });
            Grid.SetColumn(text, 1);
            content.Children.Add(text);

            if (s.IsBookmark)
            {
                Grid.SetColumn(BookmarkTag(out TextBlock tag), 2);
                content.Children.Add(tag);
            }

            Button row = new()
            {
                Content = content,
                Style = (Style)RootGrid.Resources["AvSidebarRowButton"],
                Height = 40,
            };

            int index = i;
            row.Click += (_, _) => CommitSuggestion(index);
            row.PointerEntered += (_, _) => Highlight(index);

            _suggestList.Children.Add(row);
        }
    }

    private TextBlock BookmarkTag(out TextBlock tag)
    {
        tag = new TextBlock
        {
            Text = "Favourite",
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeBrush("AvLabelTertiaryBrush"),
        };
        return tag;
    }

    private void PositionSuggestions()
    {
        if (_suggestPopup?.Child is not FrameworkElement shell)
        {
            return;
        }

        try
        {
            GeneralTransform transform = AddressField.TransformToVisual(RootGrid);
            Windows.Foundation.Point origin = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

            shell.Width = Math.Max(320, AddressField.ActualWidth);
            _suggestPopup.HorizontalOffset = origin.X;
            _suggestPopup.VerticalOffset = origin.Y + AddressField.ActualHeight + 6;
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "smart search: position list");
        }
    }

    private void Highlight(int index)
    {
        _suggestIndex = index;

        if (_suggestList is null)
        {
            return;
        }

        for (int i = 0; i < _suggestList.Children.Count; i++)
        {
            if (_suggestList.Children[i] is Button row)
            {
                row.Background = i == index
                    ? ThemeBrush("AvSidebarRowSelectedBrush")
                    : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }
    }

    private void HideSuggestions()
    {
        if (_suggestPopup is not null)
        {
            _suggestPopup.IsOpen = false;
        }

        _suggestIndex = -1;

        // A list nobody is looking at needs no suggestions: whatever was waiting to be sent is not.
        CancelEngineSuggestions();
    }

    private void CommitSuggestion(int index)
    {
        if (index < 0 || index >= _suggestions.Count)
        {
            return;
        }

        Suggestion chosen = _suggestions[index];
        string url = chosen.Url;
        HideSuggestions();

        if (_active is { IsReady: true } tab)
        {
            // Category 2: which suggestion was picked, and what had been typed to get it.
            DiagBrowse(chosen.IsSearch ? "search" : "suggestion", url, new
            {
                tab = tab.Id,
                typed = AddressBox.Text,
                picked = index,
                of = _suggestions.Count,
                kind = chosen.FromEngine ? "engine suggestion" : chosen.IsSearch ? "search" : chosen.IsBookmark ? "favourite" : "history",
                topHit = index == 0,
                query = chosen.IsSearch ? chosen.Title : null,
                engine = chosen.IsSearch ? chosen.Host : null,
                target = url,
                via = "suggestion list",
            });

            HideError();
            tab.Navigate(url);
            FocusPage(tab);
        }
    }

    /// <summary>
    /// Called on every keystroke in the address bar.
    ///
    /// Inline completion is Safari's signature here: type "wik" and the rest of a host you
    /// visit appears already selected, so Enter goes straight there and one more keystroke
    /// replaces it. Only offered for somewhere you have been more than once — completing to
    /// a page you glanced at once is worse than not completing at all.
    /// </summary>
    private void AddressBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_addressFocused)
        {
            return;
        }

        string typed = AddressBox.Text;
        ShowSuggestions(typed);

        if (_suppressAutocomplete || typed.Length < 2)
        {
            return;
        }

        // Only ever EXTEND. Completing while the caret is mid-string, or while the user is
        // deleting, fights the person typing.
        if (AddressBox.SelectionStart != typed.Length)
        {
            return;
        }

        string? host = History.InlineCompletion(typed);
        if (host is null || host.Length <= typed.Length
            || !host.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _suppressAutocomplete = true;
        try
        {
            AddressBox.Text = typed + host.Substring(typed.Length);
            AddressBox.Select(typed.Length, host.Length - typed.Length);
        }
        finally
        {
            _suppressAutocomplete = false;
        }
    }

    /// <summary>Down/Up walk the list, Enter takes the highlighted row, Escape dismisses.</summary>
    private bool HandleSuggestionKey(VirtualKey key)
    {
        if (_suggestPopup is null || !_suggestPopup.IsOpen || _suggestions.Count == 0)
        {
            return false;
        }

        switch (key)
        {
            case VirtualKey.Down:
                Highlight((_suggestIndex + 1) % _suggestions.Count);
                return true;

            case VirtualKey.Up:
                Highlight(_suggestIndex <= 0 ? _suggestions.Count - 1 : _suggestIndex - 1);
                return true;

            case VirtualKey.Escape:
                HideSuggestions();
                return true;

            case VirtualKey.Enter when _suggestIndex >= 0:
                CommitSuggestion(_suggestIndex);
                return true;
        }

        return false;
    }
}
