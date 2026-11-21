using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// Beta Prep 2 — the search bar in the middle of the Start Page (Grant, 21 Nov 2026, after studying
/// Brave's new tab page: "that bar in the middle part you can search instead of going to the very
/// top and when you type things it will kind of automatically search in that new bar and you can
/// choose to press on the other original bar for urls and links").
///
/// <para><b>What it is.</b> A search field under the Aviary mark (StartPage.cs draws it). A new tab
/// leaves the keyboard IN it rather than in the address bar, and typing anywhere on the Start Page
/// lands in it. Enter searches with the engine chosen in Settings; the address bar is untouched and
/// one click (or Ctrl+L) away for addresses. Both can be changed: the bar has a switch (Customise,
/// and Settings → Get started), and so does where a new tab's typing goes.</para>
///
/// <para><b>What it sends, which is the address bar's rules and nothing more.</b> Nothing leaves
/// the computer until Enter, unless "Search suggestions from the engine" is on — and then only to
/// the chosen engine, never from a private window, never an address
/// (<see cref="SearchSuggestions.MaySend"/>), with no cookies, no Referer and no redirects (the
/// same <see cref="SuggestHttp"/> client), after the same short pause. The page under the bar says
/// which of the two is true right now. Something typed that IS an address is opened, exactly as the
/// address bar would, and is never sent to the search engine as words.</para>
///
/// <para>The page is Aviary's own and every message carries its token, but what it sends is still
/// only ever treated as text to search for or an http(s) address to open.</para>
/// </summary>
public sealed partial class MainWindow
{
    internal const string StartSearchKey = "startSearch";
    internal const string NewTabTypingKey = "newTabTyping";
    internal const string TypingInSearchBar = "The Start Page search bar";
    internal const string TypingInAddressBar = "The address bar";

    /// <summary>How many of your own pages, and how many of the engine's suggestions, the list shows.</summary>
    private const int StartSearchLocal = 3;
    private const int StartSearchEngine = 5;

    private static bool StartSearchShown => AppSettings.Get(StartSearchKey) != "false";

    /// <summary>A new tab's typing goes to the bar: it is shown, and Settings has not said "the address bar".</summary>
    private static bool StartSearchTakesTyping =>
        StartSearchShown && !AppSettings.Get(NewTabTypingKey).StartsWith("The address", StringComparison.OrdinalIgnoreCase);

    /// <summary>What the Start Page needs to draw the bar and say truthfully what typing in it sends.</summary>
    private object StartSearchPayload() => new
    {
        engine = SearchEngines.Current.Name,
        suggests = EngineSuggestionsOn,
        typing = StartSearchTakesTyping,
    };

    /// <summary>Puts the caret in the Start Page's search bar (the page ignores it when the bar is hidden).</summary>
    private static void FocusStartSearch(BrowserTab tab)
    {
        try
        {
            if (tab.IsReady && tab.View.CoreWebView2 is CoreWebView2 core)
            {
                _ = core.ExecuteScriptAsync("window.__aviaryFocusFind&&window.__aviaryFocusFind()");
            }
        }
        catch (Exception ex) when (IsClosingWebView(ex))
        {
            // The tab closed between the request and the script.
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "start search: focus the bar");
        }
    }

    private CancellationTokenSource? _startSuggest;

    /// <summary>
    /// Words were typed in the bar. Your own matches go back at once; the engine's follow if — and
    /// only if — the suggestions switch allows these words to be sent.
    /// </summary>
    private void StartSearchTyped(CoreWebView2 page, string typed)
    {
        string query = (typed ?? string.Empty).Trim();
        _startSuggest?.Cancel();
        _startSuggest = null;

        if (query.Length == 0 || query.Length > SearchSuggestions.MaxQuery)
        {
            return;
        }

        bool isSearch = !LooksLikeUrl(query);
        List<object> local = StartSearchLocalMatches(query);
        PushStartSearch(page, query, StartSearchList(query, isSearch, new List<string>(), local));

        if (!isSearch || !EngineSuggestionsOn || !SearchSuggestions.MaySend(query))
        {
            return;
        }

        _startSuggest = new CancellationTokenSource();
        _ = StartSearchFromEngineAsync(page, query, local, SearchEngines.Current, _startSuggest.Token);
    }

    /// <summary>Favourites first, then history — the same two places the address bar looks.</summary>
    private static List<object> StartSearchLocalMatches(string query)
    {
        List<object> found = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (Bookmark bookmark in Bookmarks.Items)
        {
            if (found.Count >= StartSearchLocal)
            {
                break;
            }

            if ((bookmark.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                 || bookmark.Url.Contains(query, StringComparison.OrdinalIgnoreCase))
                && seen.Add(bookmark.Url))
            {
                found.Add(new { kind = "favourite", title = bookmark.Title, url = bookmark.Url, note = BrowserTab.HostOf(bookmark.Url) });
            }
        }

        foreach (HistoryEntry entry in History.Search(query, 6))
        {
            if (found.Count >= StartSearchLocal)
            {
                break;
            }

            if (seen.Add(entry.Url))
            {
                found.Add(new
                {
                    kind = "history",
                    title = string.IsNullOrWhiteSpace(entry.Title) ? entry.Host : entry.Title,
                    url = entry.Url,
                    note = entry.Host,
                });
            }
        }

        return found;
    }

    /// <summary>
    /// The list, top to bottom: what Enter will do, what the engine suggests (always a SEARCH for
    /// those words, never an address opened), then your own pages.
    /// </summary>
    private static List<object> StartSearchList(string query, bool isSearch, List<string> fromEngine, List<object> local)
    {
        string engine = SearchEngines.Current.Name;
        List<object> items = new();

        items.Add(isSearch
            ? new { kind = "search", title = query, url = string.Empty, note = "Search " + engine }
            : new { kind = "site", title = query, url = query, note = "Open this address" });

        foreach (string words in fromEngine.Take(StartSearchEngine))
        {
            items.Add(new { kind = "search", title = words, url = string.Empty, note = engine });
        }

        items.AddRange(local);
        return items;
    }

    private static void PushStartSearch(CoreWebView2 page, string query, List<object> items)
    {
        try
        {
            _ = page.ExecuteScriptAsync("window.__aviaryFind&&window.__aviaryFind("
                + JsonSerializer.Serialize(query) + "," + JsonSerializer.Serialize(items) + ")");
        }
        catch (Exception ex) when (IsClosingWebView(ex))
        {
            // The tab moved on or closed while the list was being made.
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "start search: show the list");
        }
    }

    private async Task StartSearchFromEngineAsync(CoreWebView2 page, string query, List<object> local,
        SearchEngines.Engine engine, CancellationToken cancel)
    {
        string url = engine.SuggestUrl + Uri.EscapeDataString(query);
        Stopwatch clock = new();

        try
        {
            // Still typing: the next keystroke cancels this, and nothing is sent.
            await Task.Delay(SuggestDelayMs, cancel);

            using HttpRequestMessage request = new(HttpMethod.Get, url);
            if (AppSettings.GetBool("gpc"))
            {
                request.Headers.TryAddWithoutValidation("Sec-GPC", "1");
            }

            clock.Start();
            using HttpResponseMessage response = await SuggestHttp.SendAsync(request, cancel);
            byte[] body = await response.Content.ReadAsByteArrayAsync(cancel);
            clock.Stop();

            // Category 3, like the address bar's: a request the browser made, the words in its address.
            Diagnostics.Request(new NetRecord
            {
                Tab = _active?.Id ?? 0,
                Url = url,
                Method = "GET",
                Context = "Suggestions",
                Status = (int)response.StatusCode,
                Type = "search suggestions",
                Bytes = body.Length,
                Ms = Math.Round(clock.Elapsed.TotalMilliseconds, 1),
                Initiator = "start page search bar",
            });

            if (!response.IsSuccessStatusCode || cancel.IsCancellationRequested)
            {
                return;
            }

            List<string> words = SearchSuggestions.Parse(body, query);
            if (words.Count > 0)
            {
                PushStartSearch(page, query, StartSearchList(query, true, words, local));
            }
        }
        catch (OperationCanceledException)
        {
            // Typing moved on, or the engine took longer than its four seconds. Your own matches stay.
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "start page search suggestions: " + engine.Name);
        }
    }

    /// <summary>
    /// Enter, or a row of the list. A picked suggestion is searched for; a picked page of your own, or
    /// something typed that is an address, is opened; anything else is searched for — the address
    /// bar's own decision (<see cref="Navigate"/>), so the two bars can never disagree.
    /// </summary>
    private void StartSearchCommit(CoreWebView2 page, JsonElement root)
    {
        _startSuggest?.Cancel();
        _startSuggest = null;

        // The bar can only be typed into on the page in front, and that page is one of Aviary's own
        // (the message carried its token). Not compared by reference: the engine object an event
        // hands over is not always the same wrapper the tab holds.
        if (_active is not { IsReady: true, IsInternalPage: true } tab)
        {
            return;
        }

        string typed = root.TryGetProperty("q", out JsonElement q) && q.ValueKind == JsonValueKind.String
            ? (q.GetString() ?? string.Empty).Trim() : string.Empty;
        string pick = root.TryGetProperty("pick", out JsonElement p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? string.Empty : string.Empty;
        string value = root.TryGetProperty("value", out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? string.Empty).Trim() : string.Empty;

        if (typed.Length > SearchSuggestions.MaxQuery * 4 || value.Length > 4096)
        {
            return;
        }

        bool went;
        if (pick == "search" && value.Length > 0)
        {
            // Always a search for those words with the chosen engine, whatever the words look like.
            string target = SearchPrefix + Uri.EscapeDataString(value);
            DiagBrowse("search", "search: " + value, new
            {
                tab = tab.Id,
                typed,
                query = value,
                engine = AppSettings.Get("searchEngine"),
                target,
                via = "start page search bar",
            });
            ClearNotice(tab);
            tab.Navigate(target);
            went = true;
        }
        else
        {
            went = Navigate(value.Length > 0 && pick.Length > 0 ? value : typed, "start page search bar");
        }

        if (went)
        {
            FocusPage(_active);
        }
    }
}
