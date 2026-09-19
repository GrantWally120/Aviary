// The engine harness (M3): every syntax FilterEngine claims to read, checked on a synthetic list;
// then the real EasyList + EasyPrivacy — build time, what is skipped and why, 16 real-world URLs,
// and the cost of 200,000 synthetic requests. "TOTAL: n passed, 0 failed" is the pass mark.
using System.Diagnostics;
using Aviary;

// "samesite": what the FIRST long-URL same-site check costs in a fresh process (M4.5 — 3.4 ms
// of UI-thread CPU on the first YouTube video request, even after the engine's warm-up).
if (args.Length > 0 && args[0] == "samesite")
{
    string longUrl = "https://rr1---sn-q4flrnlz.googlevideo.com/videoplayback?expire=1278794868&ei=bXctofU8nofAiMVK1psMuEd&ip=60.40.47.79&id=o-AAvR-jejr0lG684UaJjhEiCN0zXUvYQWhHYmjF7ffqRi&itag=243&aitags=133%2C134%2C135&source=youtube&requiressl=yes&mh=Qd&mm=31%2C29&mn=sn-q4flrnlz&ms=au%2Crdu&mv=m&mvi=3&pl=24&initcwndbps=2360000&vprv=1&mime=video%2Fwebm&rqh=1&gir=yes&clen=1234567&dur=123.456&lmt=1757830000000000&mt=1757830653&fvip=3&keepalive=yes&c=WEB&txp=4535434&sparams=expire%2Cei%2Cip%2Cid%2Caitags%2Csource&sig=AJfQdSswRQIgVx7ZmL0p2N3o4Q5R6S7T8U9V&cver=2.20260915.01.00&range=0-123456&rn=1&rbuf=0";
    FilterEngine.Page ytPage = new("www.youtube.com");
    string mode = args.Length > 1 ? args[1] : "";
    if (mode == "warm-lower") { _ = "https://Example.com/Some/Long/Path?Query=Value&More=Stuff&Even=More&And=More&Yet=More&Still=More&Keep=Going&A=1&B=2".ToLowerInvariant(); }
    if (mode == "warm-long") { FilterEngine.IsSameSite(longUrl.Replace("rr1", "rr9"), ytPage); }
    foreach (string label in new[] { "first", "second", "third" })
    {
        var t = Stopwatch.StartNew();
        FilterEngine.IsSameSite(label == "first" ? longUrl : longUrl.Replace("rr1", label == "second" ? "rr2" : "rr3"), ytPage);
        Console.WriteLine($"{mode,-10} {label}: {t.Elapsed.TotalMilliseconds * 1000:0.0} us");
    }
    var tl = Stopwatch.StartNew();
    _ = longUrl.Replace("rr1", "rr7").ToLowerInvariant();
    Console.WriteLine($"{mode,-10} ToLowerInvariant alone after: {tl.Elapsed.TotalMilliseconds * 1000:0.0} us");
    return;
}

int pass = 0, fail = 0;
void Check(string name, bool ok, string? detail = null)
{
    if (ok) { pass++; } else { fail++; Console.WriteLine("FAIL  " + name + (detail is null ? "" : "  — " + detail)); }
}

// ── synthetic list: every syntax the engine claims to read ──
string synthetic = """
[Adblock Plus 2.0]
! Title: Test
||ads.example.com^
||tracker.net^$third-party
||cdn.example.org/ads/*$script
/banner/*/img^
-ad-banner.
@@||ads.example.com/allowed^
||good.com^$domain=a.com|~b.a.com
|https://start.test/path
end.test/x.js|
||imp.test^$important
@@||imp.test^
||docblock.test^$document
@@||allowedsite.test^$document
@@||ghide.test^$generichide
||xhr.test^$xmlhttprequest
||noimg.test^$~image
||case.test/UPPER$match-case
||first.test^$~third-party
||ent.test^$domain=google.*
||sep.test/a^b
*$script,domain=allscripts.test
||wild.test/*/ads/*.js
*$ping,third-party
/webtracking/*$~document,~subdocument,domain=~ups.test
###ad-top
##.sponsored
example.com##.promo-box
example.com#@#.sponsored
~other.com,foo.com##.foo-ad
google.*##.g-ad
##div{color:red}
##+js(foo)
example.com#?#.x:has-text(ad)
/regex/
||x.test^$csp=script-src 'none'
||popup.test^$popup
##a[href="x
""";

FilterEngine e = FilterEngine.Build(new[] { new FilterEngine.Source("Test", synthetic) });
FilterEngine.Page P(string host) => new(host);
bool B(string url, ReqType t, string page, bool standard = false) => e.Match(url, t, P(page), standard).Blocked;

Check("host rule", B("https://ads.example.com/x.js", ReqType.Script, "news.com"));
Check("host rule, subdomain", B("https://sub.ads.example.com/x", ReqType.Image, "news.com"));
Check("host rule, label boundary", !B("https://notads.example.com/", ReqType.Image, "news.com"));
Check("host rule, not in query", !B("https://x.com/?u=https://ads.example.com/", ReqType.Image, "news.com"));
Check("exception", !B("https://ads.example.com/allowed/1", ReqType.Script, "news.com"));
Check("exception path boundary", B("https://ads.example.com/allowedx", ReqType.Script, "news.com"));
Check("third-party: first-party page", !B("https://tracker.net/p", ReqType.Script, "www.tracker.net"));
Check("third-party: other page", B("https://tracker.net/p", ReqType.Script, "news.com"));
Check("type script", B("https://cdn.example.org/ads/x.js", ReqType.Script, "news.com"));
Check("type not image", !B("https://cdn.example.org/ads/x.js", ReqType.Image, "news.com"));
Check("wildcard + sep", B("https://x.com/banner/123/img?x=1", ReqType.Image, "news.com"));
Check("'.' is not a separator (ABP)", !B("https://x.com/banner/123/img.gif", ReqType.Image, "news.com"));
Check("sep rejects letter", !B("https://x.com/banner/123/imgx", ReqType.Image, "news.com"));
Check("sep at end", B("https://x.com/banner/1/img", ReqType.Image, "news.com"));
Check("substring", B("https://x.com/some-ad-banner.png", ReqType.Image, "news.com"));
Check("domain include", B("https://good.com/x", ReqType.Image, "a.com"));
Check("domain include sub", B("https://good.com/x", ReqType.Image, "www.a.com"));
Check("domain exclude", !B("https://good.com/x", ReqType.Image, "b.a.com"));
Check("domain other", !B("https://good.com/x", ReqType.Image, "c.com"));
Check("start anchor", B("https://start.test/path/more", ReqType.Image, "n.com"));
Check("start anchor, not mid", !B("https://x.com/?u=https://start.test/path", ReqType.Image, "n.com"));
Check("end anchor", B("https://y.com/end.test/x.js", ReqType.Script, "n.com"));
Check("end anchor, not before query", !B("https://y.com/end.test/x.js?v=1", ReqType.Script, "n.com"));
Check("important beats exception", B("https://imp.test/a", ReqType.Script, "n.com"));
Check("xhr type", B("https://xhr.test/a", ReqType.Xhr, "n.com"));
Check("xhr type not script", !B("https://xhr.test/a", ReqType.Script, "n.com"));
Check("~image skips image", !B("https://noimg.test/a", ReqType.Image, "n.com"));
Check("~image keeps script", B("https://noimg.test/a", ReqType.Script, "n.com"));
Check("match-case", B("https://case.test/UPPER", ReqType.Image, "n.com"));
Check("match-case lower", !B("https://case.test/upper", ReqType.Image, "n.com"));
Check("~third-party on own site", B("https://first.test/a", ReqType.Image, "www.first.test"));
Check("~third-party elsewhere", !B("https://first.test/a", ReqType.Image, "n.com"));
Check("domain entity google.*", B("https://ent.test/a", ReqType.Image, "www.google.co.uk"));
Check("domain entity google.* com", B("https://ent.test/a", ReqType.Image, "google.com"));
Check("domain entity other", !B("https://ent.test/a", ReqType.Image, "notgoogle.com"));
Check("^ between literals", B("https://sep.test/a/b", ReqType.Image, "n.com"));
Check("^ between literals, letter", !B("https://sep.test/axb", ReqType.Image, "n.com"));
Check("patternless domain rule", B("https://cdn.any/lib.js", ReqType.Script, "allscripts.test"));
Check("patternless domain rule other page", !B("https://cdn.any/lib.js", ReqType.Script, "n.com"));
Check("patternless domain rule other type", !B("https://cdn.any/pic.png", ReqType.Image, "allscripts.test"));
Check("wild middle", B("https://wild.test/x/y/ads/z/q.js", ReqType.Script, "n.com"));
Check("wild middle no", !B("https://wild.test/x/y/add/z/q.js", ReqType.Script, "n.com"));
Check("main-frame document never by plain rule", !B("https://ads.example.com/", ReqType.Document, "ads.example.com"));
Check("$document strict page", e.MatchPage("https://docblock.test/").Blocked);
Check("$document frame", B("https://docblock.test/frame", ReqType.Subdocument, "n.com"));
Check("$document not script", !B("https://docblock.test/x.js", ReqType.Script, "n.com"));
Check("page allowed by $document exception", e.PageFor("https://allowedsite.test/").Allowed);
Check("allowed page: requests pass", !e.Match("https://ads.example.com/x.js", ReqType.Script, e.PageFor("https://allowedsite.test/"), false).Blocked);
Check("standard: same site not blocked", !B("https://ads.example.com/x", ReqType.Script, "www.example.com", standard: true));
Check("strict: same site blocked", B("https://ads.example.com/x", ReqType.Script, "www.example.com", standard: false));
Check("standard: other site blocked", B("https://ads.example.com/x", ReqType.Script, "news.com", standard: true));
Check("bare scheme", !B("https://", ReqType.Script, "n.com"));
Check("ip host", !B("http://127.0.0.1:8080/ads", ReqType.Script, "n.com"));
Check("ws scheme", B("wss://ads.example.com/socket", ReqType.WebSocket, "n.com"));
Check("port", B("https://ads.example.com:8443/x", ReqType.Script, "n.com"));
Check("verdict names rule", e.Match("https://ads.example.com/x", ReqType.Script, P("n.com"), false).Rule == "||ads.example.com^");
Check("verdict names list", e.Match("https://ads.example.com/x", ReqType.Script, P("n.com"), false).List == "Test");

Check("third-party pings", B("https://t.other/p", ReqType.Ping, "n.com"));
Check("third-party pings: not images", !B("https://t.other/p.png", ReqType.Image, "n.com"));
Check("first-party ping kept", !B("https://www.n.com/p", ReqType.Ping, "n.com"));
Check("~document negation read", B("https://x.test/webtracking/a.js", ReqType.Script, "n.com"));
Check("~document domain exclude", !B("https://x.test/webtracking/a.js", ReqType.Script, "ups.test"));
Check("strict page: plain host", e.MatchPage("https://ads.example.com/").Blocked);
Check("strict page: plain host subdomain", e.MatchPage("https://x.ads.example.com/p").Blocked);
Check("strict page: exception path wins", !e.MatchPage("https://ads.example.com/allowed/x").Blocked);
Check("strict page: ordinary site", !e.MatchPage("https://news.com/").Blocked);
Check("same site", FilterEngine.IsSameSite("https://cdn.news.co.uk/a.js", new FilterEngine.Page("www.news.co.uk")));
Check("not same site", !FilterEngine.IsSameSite("https://news.com/a.js", new FilterEngine.Page("www.news.co.uk")));
Check("same site, port", FilterEngine.IsSameSite("http://a.news.com:8080/x", new FilterEngine.Page("news.com")));
var st = e.Stats;
Check("skipped: regex", st.SkippedBy.ContainsKey("regular expression"));
Check("skipped: csp", st.SkippedBy.ContainsKey("$csp"));
Check("skipped: popup", st.SkippedBy.ContainsKey("$popup"));
Check("skipped: unsafe selector", st.SkippedBy.TryGetValue("unsafe or malformed selector", out int u) && u == 2, "unsafe=" + u);
Check("skipped: scriptlet", st.SkippedBy.ContainsKey("scriptlet or HTML filter"));
Check("skipped: extended", st.SkippedBy.Keys.Any(k => k.StartsWith("extended element hiding")));

var c = e.Cosmetic;
Check("generic has #ad-top", c.Generic.Split('\n').Contains("#ad-top"));
Check("generic has .sponsored", c.Generic.Split('\n').Contains(".sponsored"));
Check("specific example.com", c.Specific.Contains("\nexample.com\t.promo-box\n"));
Check("exception example.com", c.Exceptions.Contains("\nexample.com\t.sponsored"));
Check("specific foo.com", c.Specific.Contains("\nfoo.com\t.foo-ad\n"));
Check("negated → exception", c.Exceptions.Contains("\nother.com\t.foo-ad"));
Check("entity key", c.Specific.Contains("\ngoogle.*\t.g-ad\n"));
Check("page rule gh", c.PageRules.Any(r => (string)r[0] == "gh"));
Check("page rule doc", c.PageRules.Any(r => (string)r[0] == "doc"));
string script = e.CosmeticScript(new[] { "down.test" }, new[] { "collection.aviary" });
Check("script has data", script.Contains("\"off\":[\"down.test\"]") && script.Contains("promo-box"));

Console.WriteLine($"synthetic: {pass} passed, {fail} failed");

// ── the real lists ──
// bin\Release\net9.0\ → tools\test\filters → the repo's src\Aviary\Filters\.
string dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "src", "Aviary", "Filters")) + Path.DirectorySeparatorChar;
var sources = new[]
{
    new FilterEngine.Source("EasyList", File.ReadAllText(dir + "easylist.txt")),
    new FilterEngine.Source("EasyPrivacy", File.ReadAllText(dir + "easyprivacy.txt")),
};
for (int warm = 0; warm < 2; warm++)
{
    GC.Collect(); long before = GC.GetTotalMemory(true);
    var sw = Stopwatch.StartNew();
    FilterEngine real0 = FilterEngine.Build(sources);
    sw.Stop();
    long after = GC.GetTotalMemory(true);
    Console.WriteLine($"build #{warm}: {sw.ElapsedMilliseconds} ms (engine says {real0.BuildMs:0} ms), managed heap +{(after - before) / 1024 / 1024} MB");
    GC.KeepAlive(real0);
}
FilterEngine real = FilterEngine.Build(sources);

// ── a long YouTube video-stream request (M4.5: one took 5-6 ms in the app, UI thread) ──
{
    string vp = "https://rr3---sn-q4flrnld.googlevideo.com/videoplayback?expire=4986042948&ei=WNw83dmQt5Hyn7uVdbOYu2d&ip=24.70.45.94&id=o-AAvR-jejr0lG684UaJjhEiCN0zXUvYQWhHYmjF7ffqRi&itag=243&aitags=133%2C134%2C135%2C136%2C160%2C242%2C243%2C244%2C247%2C278&source=youtube&requiressl=yes&xpc=EgVo2aDSNQ%3D%3D&met=1757830927%2C&mh=Qd&mm=31%2C29&mn=sn-q4flrnld%2Csn-q4fzen7e&ms=au%2Crdu&mv=m&mvi=3&pl=24&rms=au%2Cau&initcwndbps=2360000&bui=AY1jyLNnVxNcHTo6&spc=hcYD5bKq1Vp9&vprv=1&svpuc=1&mime=video%2Fwebm&rqh=1&gir=yes&clen=1234567&dur=123.456&lmt=1757830000000000&mt=1757830653&fvip=3&keepalive=yes&fexp=51331020&c=WEB&sefc=1&txp=4535434&n=abcDEFghi&sparams=expire%2Cei%2Cip%2Cid%2Caitags%2Csource%2Crequiressl%2Cxpc%2Cbui%2Cspc%2Cvprv%2Csvpuc%2Cmime%2Crqh%2Cgir%2Cclen%2Cdur%2Clmt&sig=AJfQdSswRQIgVx7ZmL0p2N3o4Q5R6S7T8U9V0W1X2Y3Z4a5b6c7d8e9f0CIQD&lsparams=met%2Cmh%2Cmm%2Cmn%2Cms%2Cmv%2Cmvi%2Cpl%2Crms%2Cinitcwndbps&lsig=APaTxxMwRgIhAK1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6q7r8s9t0u1v2&alr=yes&cpn=AbCdEfGhIjKlMnOp&cver=2.20260915.01.00&range=0-123456&rn=1&rbuf=0";
    FilterEngine.Page yt = new("www.youtube.com");
    foreach (bool standard in new[] { true, false })
    {
        for (int w = 0; w < 50; w++) { real.Match(vp, ReqType.Xhr, yt, standard); }
        var lw = Stopwatch.StartNew();
        const int n = 2000;
        for (int k = 0; k < n; k++) { real.Match(vp, ReqType.Xhr, yt, standard); }
        double us = lw.Elapsed.TotalMilliseconds * 1000 / n;
        Console.WriteLine($"long URL ({vp.Length} chars), {(standard ? "Standard" : "Strict")}: {us:0.0} us per match");
        Check("long URL under 200 us (" + (standard ? "Standard" : "Strict") + ")", us < 200, $"{us:0.0} us");
    }
    var buckets = real.BucketsFor(vp);
    Console.WriteLine($"   tokens with buckets: {buckets.Count}, rules in them: {buckets.Sum(b => b.Rules)}; biggest: " +
        string.Join(", ", buckets.OrderByDescending(b => b.Rules).Take(8).Select(b => b.Token + "=" + b.Rules)));
}

var s = real.Stats;
Console.WriteLine($"lines={s.Lines} network={s.Network} exc={s.NetworkExceptions} hostOnly={s.HostOnly} generic={s.CosmeticGeneric} specific={s.CosmeticSpecific} cexc={s.CosmeticExceptions} page={s.PageExceptions} skipped={s.Skipped}");
foreach (var kv in s.SkippedBy.OrderByDescending(k => k.Value))
{
    Console.WriteLine($"   skipped {kv.Value,5}  {kv.Key}");
    if (s.SkippedExamples.TryGetValue(kv.Key, out var ex)) { foreach (var x in ex) { Console.WriteLine("            e.g. " + (x.Length > 110 ? x[..110] + "…" : x)); } }
}

(string url, ReqType t, string page, bool blocked)[] known =
{
    ("https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js?client=ca-pub-1", ReqType.Script, "news.com", true),
    ("https://www.google-analytics.com/analytics.js", ReqType.Script, "news.com", true),
    ("https://securepubads.g.doubleclick.net/tag/js/gpt.js", ReqType.Script, "news.com", true),
    ("https://connect.facebook.net/en_US/fbevents.js", ReqType.Script, "news.com", true),
    ("https://www.googletagmanager.com/gtm.js?id=GTM-ABC", ReqType.Script, "news.com", true),
    ("https://static.hotjar.com/c/hotjar-1.js?sv=6", ReqType.Script, "news.com", true),
    ("https://cdn.taboola.com/libtrc/site/loader.js", ReqType.Script, "news.com", true),
    ("https://code.jquery.com/jquery-3.7.1.min.js", ReqType.Script, "news.com", false),
    ("https://cdn.jsdelivr.net/npm/vue@3/dist/vue.global.js", ReqType.Script, "news.com", false),
    ("https://fonts.googleapis.com/css2?family=Inter", ReqType.Stylesheet, "news.com", false),
    ("https://fonts.gstatic.com/s/inter/v12/a.woff2", ReqType.Font, "news.com", false),
    ("https://ajax.googleapis.com/ajax/libs/jquery/3.6.0/jquery.min.js", ReqType.Script, "news.com", false),
    ("https://i.ytimg.com/vi/abc/hqdefault.jpg", ReqType.Image, "www.youtube.com", false),
    ("https://www.youtube.com/watch?v=abc", ReqType.Document, "www.youtube.com", false),
    ("https://upload.wikimedia.org/wikipedia/commons/a/a9/Example.jpg", ReqType.Image, "en.wikipedia.org", false),
    ("https://github.githubassets.com/assets/app.js", ReqType.Script, "github.com", false),
};
foreach (var k in known)
{
    var v = real.Match(k.url, k.t, new FilterEngine.Page(k.page), false);
    Check("real: " + k.url, v.Blocked == k.blocked, v.Rule + " (" + v.List + ")");
}

// ── cost: a synthetic corpus of 200,000 requests ──
var rnd = new Random(42);
string[] blockedHosts = File.ReadAllLines(dir + "easyprivacy.txt").Concat(File.ReadAllLines(dir + "easylist.txt"))
    .Where(l => l.StartsWith("||") && l.EndsWith("^") && !l.Contains('/') && !l.Contains('*')).Select(l => l[2..^1]).ToArray();
string[] okHosts = { "cdn.jsdelivr.net", "fonts.gstatic.com", "i.ytimg.com", "upload.wikimedia.org", "static.cnn.com", "img.bbc.co.uk", "media.guim.co.uk", "assets.nytimes.com", "cdn.shopify.com", "s.yimg.com" };
string[] words = { "static", "js", "css", "img", "v2", "assets", "build", "main", "vendor", "chunk", "app", "lib", "media", "photo", "news", "article", "2026", "09", "thumb", "large", "player", "widget", "embed", "api", "track", "event", "pixel", "ad", "sponsor", "promo" };
string[] ext = { ".js", ".css", ".png", ".jpg", ".webp", ".woff2", ".json", "", ".gif", ".svg" };
ReqType[] types = { ReqType.Script, ReqType.Image, ReqType.Stylesheet, ReqType.Xhr, ReqType.Font, ReqType.Subdocument, ReqType.Media, ReqType.Ping };
var corpus = new List<(string, ReqType)>();
for (int i = 0; i < 200_000; i++)
{
    string host = rnd.Next(5) == 0 ? blockedHosts[rnd.Next(blockedHosts.Length)] : okHosts[rnd.Next(okHosts.Length)];
    int segs = rnd.Next(1, 6);
    string path = string.Join("/", Enumerable.Range(0, segs).Select(_ => words[rnd.Next(words.Length)] + (rnd.Next(3) == 0 ? "-" + rnd.Next(1000) : "")));
    string q = rnd.Next(3) == 0 ? "?v=" + rnd.Next(99999) + "&id=" + Guid.NewGuid().ToString("N")[..12] : "";
    corpus.Add(("https://" + host + "/" + path + ext[rnd.Next(ext.Length)] + q, types[rnd.Next(types.Length)]));
}
var page = new FilterEngine.Page("www.news-site.com");
foreach (bool standard in new[] { false, true })
{
    for (int pass2 = 0; pass2 < 2; pass2++)
    {
        int blocked = 0; long worst = 0;
        var sw2 = Stopwatch.StartNew();
        foreach (var (url, t) in corpus)
        {
            long t0 = Stopwatch.GetTimestamp();
            if (real.Match(url, t, page, standard).Blocked) { blocked++; }
            long d = Stopwatch.GetTimestamp() - t0;
            if (d > worst) { worst = d; }
        }
        sw2.Stop();
        if (pass2 == 1)
        {
            Console.WriteLine($"{(standard ? "standard" : "strict  ")}: {corpus.Count} requests, {blocked} blocked, avg {sw2.Elapsed.TotalMilliseconds * 1000 / corpus.Count:0.00} µs, worst {worst * 1_000_000.0 / Stopwatch.Frequency:0.0} µs");
        }
    }
}

var sw3 = Stopwatch.StartNew();
string realScript = real.CosmeticScript(Array.Empty<string>(), new[] { "collection.aviary" });
Console.WriteLine($"cosmetic script: {realScript.Length / 1024} KB, built in {sw3.ElapsedMilliseconds} ms");
// (The compiled page script can be written out here to inspect it.)

Console.WriteLine($"TOTAL: {pass} passed, {fail} failed");
