using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Aviary;

/// <summary>
/// The rules of engine suggestions that need no window (M4): what may be sent, and how an
/// engine's answer is read. Kept apart from MainWindow.SearchSuggest.cs so the test harness in
/// tools/test/search/ compiles exactly this file and asks the real engines.
/// </summary>
internal static class SearchSuggestions
{
    /// <summary>Longest words ever sent, and longest suggestion ever shown. A pasted paragraph is a search, not something to suggest for.</summary>
    public const int MaxQuery = 120;

    /// <summary>At most this many rows from the engine.</summary>
    public const int MaxShown = 4;

    /// <summary>
    /// The client every suggestion request goes through (one for the whole app, for the socket
    /// reasons Favicons gives). Cookies OFF both ways, so an engine can neither set nor read one
    /// and nothing ties a keystroke to your next visit; redirects NOT followed, because the address
    /// is the engine's own published one and a redirect would take your typing somewhere the
    /// Settings row never named; no Referer, because HttpClient sends none.
    /// </summary>
    public static System.Net.Http.HttpClient CreateClient(string userAgent)
    {
        System.Net.Http.HttpClient http = new(new System.Net.Http.SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(3),
        })
        {
            Timeout = TimeSpan.FromSeconds(4),

            // A suggestion list is a few hundred bytes. Anything past this is not one.
            MaxResponseContentBufferSize = 64 * 1024,
        };

        http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return http;
    }

    /// <summary>What the address bar treats as an address rather than words to search for.</summary>
    public static bool LooksLikeUrl(string text) =>
        !text.Contains(' ') && (text.Contains('.') || text.Contains("://", StringComparison.Ordinal));

    /// <summary>
    /// Whether these words may leave the machine at all. LooksLikeUrl catches most addresses;
    /// this also keeps back what it lets through — localhost:8080, C:\Users\…, about:blank,
    /// 192.168.1.1 — none of which is a search, and some of which say where you are.
    /// </summary>
    public static bool MaySend(string query)
    {
        if (query.Length == 0 || query.Length > MaxQuery || LooksLikeUrl(query))
        {
            return false;
        }

        bool oneWord = !query.Contains(' ');
        return !(oneWord && (query.Contains(':') || query.Contains('/') || query.Contains('\\')));
    }

    /// <summary>
    /// The OpenSearch suggestions format: <c>["words", ["suggestion", …], …]</c>. Anything else
    /// gives an empty list; malformed JSON throws, for the caller to record. Entries are cleaned,
    /// de-duplicated, never the words themselves, and capped at twice what is shown — enough to
    /// still fill the list after the next keystroke filters it.
    /// </summary>
    public static List<string> Parse(byte[] body, string query)
    {
        List<string> found = new();

        // A byte-order mark is legal before JSON on the wire and JsonDocument will not skip it.
        ReadOnlyMemory<byte> json = body.AsMemory();
        if (json.Length >= 3 && body[0] == 0xEF && body[1] == 0xBB && body[2] == 0xBF)
        {
            json = json.Slice(3);
        }

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 2
            || root[1].ValueKind != JsonValueKind.Array)
        {
            return found;
        }

        foreach (JsonElement item in root[1].EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string s = Clean(item.GetString());
            if (s.Length == 0 || s.Length > MaxQuery
                || string.Equals(s, query.Trim(), StringComparison.OrdinalIgnoreCase)
                || found.Exists(f => string.Equals(f, s, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            found.Add(s);
            if (found.Count == MaxShown * 2)
            {
                break;
            }
        }

        return found;
    }

    /// <summary>
    /// Control characters and the bidirectional overrides out (text that draws itself backwards
    /// has no business in a list of searches), runs of whitespace to one space, trimmed. Joiners
    /// stay: several scripts, and emoji, need them.
    /// </summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        StringBuilder clean = new(text.Length);
        bool space = false;
        foreach (char c in text)
        {
            if (c is >= '‪' and <= '‮' or >= '⁦' and <= '⁩')
            {
                continue;
            }

            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                space = clean.Length > 0;
                continue;
            }

            if (space)
            {
                clean.Append(' ');
                space = false;
            }

            clean.Append(c);
        }

        return clean.ToString();
    }
}
