using System;
using System.Collections.Generic;
using System.Linq;

namespace Aviary;

/// <summary>
/// Every search engine Aviary offers, and the only place that knows their addresses
/// (Phase 5 M4 — "Brave Search and more safe search engines").
///
/// Until M4 the table was two copies of the same switch in MainWindow, one read at start-up and
/// one when Settings changed it, and the Settings list was a third, hand-typed. Now the Settings
/// list, the address bar, the context menu's "Search … for" and the suggestion fetcher all read
/// this, so an engine cannot be offered in one place and missing in another.
///
/// Addresses are each engine's own, from its OpenSearch description (checked 14 Sep 2026), minus
/// the parameters that tag a search as coming from a browser add-on — Ecosia's addon=opensearch,
/// Startpage's pl=opensearch. Aviary tells an engine the words and nothing else.
///
/// Suggestions: every engine here but Mojeek answers the OpenSearch suggestions format,
/// <c>["what you typed", ["suggestion", …]]</c> (Google's and Bing's add fields after it, which are
/// ignored). Mojeek publishes no suggestion service, so while it is the engine nothing is sent as
/// you type, whatever the switch says. Google's <c>client=</c> on the suggestion address chooses
/// that format; without it Google answers something else.
///
/// Qwant was on the plan and was built and tested, then left out at Grant's word (14 Sep 2026): its
/// results page answers "not yet available in your country" from the Philippines, so it could
/// suggest but never search. A saved "Qwant" falls back to DuckDuckGo like any other unknown name.
///
/// Order is the order Settings lists them: DuckDuckGo stays the default (Grant, 14 Sep), the four
/// that say they keep no profile of you follow it, then Bing and Google, which stay on the list.
/// </summary>
internal static class SearchEngines
{
    internal sealed record Engine(string Name, string SearchUrl, string? SuggestUrl)
    {
        public bool Suggests => SuggestUrl is not null;
    }

    public const string DefaultName = "DuckDuckGo";

    public static readonly IReadOnlyList<Engine> All = new Engine[]
    {
        new("DuckDuckGo",   "https://duckduckgo.com/?q=",                 "https://duckduckgo.com/ac/?type=list&q="),
        new("Brave Search", "https://search.brave.com/search?q=",         "https://search.brave.com/api/suggest?q="),
        new("Startpage",    "https://www.startpage.com/sp/search?query=", "https://www.startpage.com/osuggestions?q="),
        new("Mojeek",       "https://www.mojeek.com/search?q=",           null),
        new("Ecosia",       "https://www.ecosia.org/search?q=",           "https://ac.ecosia.org/autocomplete?type=list&q="),
        new("Bing",         "https://www.bing.com/search?q=",             "https://api.bing.com/osjson.aspx?query="),
        new("Google",       "https://www.google.com/search?q=",           "https://suggestqueries.google.com/complete/search?client=firefox&q="),
    };

    /// <summary>The engine of that name; the default for anything else, a hand-edited settings.json included.</summary>
    public static Engine Get(string? name) =>
        All.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? All[0];

    /// <summary>The engine chosen in Settings.</summary>
    public static Engine Current => Get(AppSettings.Get("searchEngine"));

    /// <summary>For the Settings page: every engine's name, and whether it can suggest.</summary>
    public static object Describe() => All.Select(e => new { name = e.Name, suggests = e.Suggests }).ToArray();
}
