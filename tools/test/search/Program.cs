// M4's harness: the rules of engine suggestions, then every engine asked for real.
// Compiles the shipping SearchEngines.cs + SearchSuggestions.cs (see the .csproj).
using System.Diagnostics;
using System.Text;
using Aviary;

int failed = 0, passed = 0;
void Check(string what, bool ok)
{
    if (ok) { passed++; } else { failed++; Console.WriteLine("  FAIL  " + what); }
}
byte[] B(string s) => Encoding.UTF8.GetBytes(s);

Console.WriteLine("== What may be sent");
foreach (var (q, want) in new (string, bool)[]
{
    ("aviary bird", true), ("weather", true), ("c++", true), ("what is 3/4", true), ("ratio 16:9 screens", true),
    ("", false), ("wikipedia.org", false), ("https://example.com", false), ("localhost:8080", false),
    ("192.168.1.1", false), ("C:\\Users\\user", false), ("about:blank", false), ("aviary:settings", false),
    ("intranet/wiki", false), (new string('a', 121), false), (new string('a', 120), true),
})
{
    Check($"MaySend(\"{(q.Length > 30 ? q[..30] + "…" : q)}\") = {want}", SearchSuggestions.MaySend(q) == want);
}

Console.WriteLine("== Reading an engine's answer");
Check("plain OpenSearch", string.Join("|", SearchSuggestions.Parse(B("[\"av\",[\"aviary\",\"avatar\"]]"), "av")) == "aviary|avatar");
Check("byte-order mark skipped", SearchSuggestions.Parse(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(B("[\"a\",[\"ab\"]]")).ToArray(), "a").Count == 1);
Check("Google's extra fields ignored", SearchSuggestions.Parse(B("[\"a\",[\"ab\",\"ac\"],[],{\"google:suggestsubtypes\":[[512]]}]"), "a").Count == 2);
Check("the words themselves dropped", SearchSuggestions.Parse(B("[\"aviary bird\",[\"aviary bird\",\"Aviary Bird\",\"aviary birds\"]]"), "aviary bird").SequenceEqual(new[] { "aviary birds" }));
Check("duplicates dropped", SearchSuggestions.Parse(B("[\"a\",[\"ab\",\"AB\",\"ab\"]]"), "a").Count == 1);
Check("non-strings skipped", SearchSuggestions.Parse(B("[\"a\",[1,null,{\"x\":1},\"ab\"]]"), "a").SequenceEqual(new[] { "ab" }));
Check("control characters and runs of space", SearchSuggestions.Parse(B("[\"a\",[\"  a\\u0007b \\t\\n c  \"]]"), "a").SequenceEqual(new[] { "a b c" }));
Check("bidi overrides removed, joiners kept", SearchSuggestions.Clean("ab\u202Ecd\u200Def") == "abcd\u200Def");
Check("too long dropped", SearchSuggestions.Parse(B("[\"a\",[\"" + new string('x', 121) + "\",\"ab\"]]"), "a").SequenceEqual(new[] { "ab" }));
Check("capped at twice what is shown", SearchSuggestions.Parse(B("[\"a\",[" + string.Join(",", Enumerable.Range(0, 20).Select(i => "\"a" + i + "\"")) + "]]"), "a").Count == SearchSuggestions.MaxShown * 2);
Check("an object is not a list", SearchSuggestions.Parse(B("{\"a\":[\"b\"]}"), "a").Count == 0);
Check("no second element", SearchSuggestions.Parse(B("[\"a\"]"), "a").Count == 0);
bool threw = false;
try { SearchSuggestions.Parse(B("<html>not json</html>"), "a"); } catch (System.Text.Json.JsonException) { threw = true; }
Check("an HTML page throws, for the caller to record", threw);

Console.WriteLine("== The table");
Check("seven engines — Qwant left out (14 Sep)", SearchEngines.All.Count == 7 && SearchEngines.Get("Qwant").Name == "DuckDuckGo");
Check("DuckDuckGo first and default", SearchEngines.All[0].Name == SearchEngines.DefaultName && SearchEngines.Get("nonsense").Name == "DuckDuckGo");
Check("names are unique", SearchEngines.All.Select(e => e.Name).Distinct().Count() == SearchEngines.All.Count);
Check("lookup ignores case", SearchEngines.Get("brave search").Name == "Brave Search");
Check("only Mojeek has no suggestions", SearchEngines.All.Where(e => !e.Suggests).Select(e => e.Name).SequenceEqual(new[] { "Mojeek" }));
foreach (SearchEngines.Engine e in SearchEngines.All)
{
    Check(e.Name + ": https, words go on the end", e.SearchUrl.StartsWith("https://") && e.SearchUrl.EndsWith("="));
    Check(e.Name + ": no add-on tag in the results address", !e.SearchUrl.Contains("opensearch") && !e.SearchUrl.Contains("addon"));
    if (e.SuggestUrl is not null) { Check(e.Name + ": suggestions over https", e.SuggestUrl.StartsWith("https://") && e.SuggestUrl.EndsWith("=")); }
}

Console.WriteLine($"offline: {passed} passed, {failed} failed");
if (args.Contains("offline")) { return failed == 0 ? 0 : 1; }

Console.WriteLine("== Every engine, asked for real (the app's own client: no cookies, no redirects)");
const string Ua = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15";
using HttpClient http = SearchSuggestions.CreateClient(Ua);
int answered = 0, suggesters = 0;
foreach (SearchEngines.Engine e in SearchEngines.All.Where(e => e.Suggests))
{
    suggesters++;
    foreach (string q in new[] { "aviary bird", "weather" })
    {
        Stopwatch clock = Stopwatch.StartNew();
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, e.SuggestUrl + Uri.EscapeDataString(q));
            request.Headers.TryAddWithoutValidation("Sec-GPC", "1");
            using HttpResponseMessage response = await http.SendAsync(request);
            byte[] body = await response.Content.ReadAsByteArrayAsync();
            List<string> got = SearchSuggestions.Parse(body, q);
            string cookie = response.Headers.Contains("Set-Cookie") ? " (tried to set a cookie — refused)" : "";
            Console.WriteLine($"  {e.Name,-13} \"{q}\" {(int)response.StatusCode} {clock.ElapsedMilliseconds,5} ms  {got.Count} → {string.Join(" · ", got.Take(3))}{cookie}");
            if (q == "aviary bird" && response.IsSuccessStatusCode && got.Count > 0) { answered++; }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  {e.Name,-13} \"{q}\" FAILED after {clock.ElapsedMilliseconds} ms: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
Console.WriteLine($"online: {answered} of {suggesters} engines suggested something for \"aviary bird\" (Startpage often answers nothing for it)");
return failed == 0 ? 0 : 1;
