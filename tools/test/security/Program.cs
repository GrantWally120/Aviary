using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Aviary;

if (args.FirstOrDefault() == "native-tls")
{
    await TlsFixture.RunAsync(args[1]);
    return;
}

if (args.FirstOrDefault() == "policy")
{
    Console.Write(PrivacyPolicy.Markdown());
    return;
}

int passed = 0, failed = 0;
void Check(string name, bool ok)
{
    if (ok) passed++; else { failed++; Console.WriteLine("FAIL: " + name); }
}
void Masked(string name, string value, string secret) => Check(name, !Diagnostics.Scrub(value).Contains(secret, StringComparison.Ordinal));

Masked("OAuth query code", "https://login.example/callback?code=SECRET_CODE&state=SECRET_STATE", "SECRET_CODE");
Masked("OAuth state", "https://login.example/callback?state=SECRET_STATE&next=home", "SECRET_STATE");
Masked("OIDC nonce", "https://login.example/auth?nonce=SECRET_NONCE", "SECRET_NONCE");
Masked("PKCE verifier", "code_verifier=SECRET_VERIFIER", "SECRET_VERIFIER");
Masked("JSON credential", "Failure: {\"access_token\": \"SECRET_JSON\", \"error\": \"bad\"}", "SECRET_JSON");
Masked("JSON escaped credential", "{\"refresh_token\":\"SECRET\\\"JSON\"}", "SECRET");
Masked("Cookie header", "Cookie: sessionid=SECRET_COOKIE; other=abc", "SECRET_COOKIE");
Masked("Set-Cookie header", "Set-Cookie: id=SECRET_HEADER; Secure", "SECRET_HEADER");
Masked("Short HTTP authorization", "Authorization: Basic SHORT", "SHORT");
Masked("Bearer in error", "Failure Bearer abcdefghijklmnopqrstuv", "abcdefghijklmnopqrstuv");
Masked("Basic auth in URL", "https://user:SECRET_PASS@example.test/path", "SECRET_PASS");
Masked("Nested encoded userinfo", "https://example.test/?next=https%3A%2F%2Fuser%3ASECRET_NEST%40other.test%2F", "SECRET_NEST");
Masked("Double encoded login code", "https://example.test/?next=https%253A%252F%252Fother.test%252F%253Fcode%253DSECRET_DOUBLE", "SECRET_DOUBLE");
Masked("Card", "card 4111 1111 1111 1111", "4111");
Check("Ordinary search retained", Diagnostics.Scrub("https://example.test/?q=ordinary+birds&zipcode=1234") == "https://example.test/?q=ordinary+birds&zipcode=1234");
Check("Ordinary JSON retained", Diagnostics.Scrub("{\"message\":\"test\"}") == "{\"message\":\"test\"}");
Check("Bounded long error", Diagnostics.Scrub(new string('a', 50_000)).Length < 4100);

foreach (var item in new[] {
    ("https://example.test/", true), ("http://example.test/", false), ("http://127.0.0.1:1234/", true),
    ("http://[::1]/", true), ("https://u:p@example.test/", false), ("file:///C:/private.txt", false),
    ("data:text/html,x", false), ("about:blank", false) })
    Check("Permission origin " + item.Item1, SecurityPolicy.IsSecurePermissionOrigin(item.Item1) == item.Item2);
Check("Internal opaque source accepted", SecurityPolicy.IsInternalMessageSource("about:blank", "about:blank"));
Check("Website replay rejected", !SecurityPolicy.IsInternalMessageSource("about:blank", "https://evil.test/"));
Check("Network sender rejected", !SecurityPolicy.IsInternalMessageSource("https://evil.test/", "about:blank"));
Check("Icon current document accepted", SecurityPolicy.IsCurrentWebDocument("https://example.test/path#icon", "https://example.test/path"));
Check("Icon stale document rejected", !SecurityPolicy.IsCurrentWebDocument("https://example.test/old", "https://example.test/new"));
Check("Icon forged origin rejected", !SecurityPolicy.IsCurrentWebDocument("https://evil.test/", "https://example.test/"));
Check("Icon nonweb origin rejected", !SecurityPolicy.IsCurrentWebDocument("about:blank", "about:blank"));
foreach (var item in new[] {
    ("https://easylist.to/easylist/easylist.txt", false, true), ("https://secure.fanboy.co.nz/list.txt", false, true),
    ("http://easylist.to/list.txt", false, false), ("https://easylist.to.evil.test/list", false, false),
    ("https://user@easylist.to/list", false, false), ("https://easylist.to:444/list", false, false),
    ("http://127.0.0.1:9876/list", true, true), ("http://example.test/list", true, false),
    ("https://evil.test/list", true, false), ("https://easylist.to/list#fragment", false, false) })
    Check("Filter destination " + item.Item1, SecurityPolicy.IsAllowedFilterUri(new Uri(item.Item1), item.Item2) == item.Item3);

TrackerBlocker.Enabled = true;
AppSettings.Set("blockMode", "Standard");
SiteSettings.Set("example.test", Shields.SiteKey, "true");
SiteSettings.Set("example.test", Shields.ModeKey, "Strict");
int writes = SiteSettings.Writes;
Shields.Set("example.test", false, true);
Check("Private up overrides saved down", !Shields.IsDown("example.test", true));
Check("Normal down preserved", Shields.IsDown("example.test", false));
Shields.SetMode("example.test", Shields.Mode.Standard, true);
Check("Private mode override", Shields.ModeFor("example.test", true) == Shields.Mode.Standard);
Shields.ResetSite("example.test", true);
Check("Private reset uses global default", Shields.ModeFor("example.test", true) == Shields.Mode.Standard);
Check("No private writes to persistent site store", SiteSettings.Writes == writes);
Check("Saved strict retained", SiteSettings.Get("example.test", Shields.ModeKey) == "Strict");
Shields.ForgetPrivate();
Check("New private session inherits unchanged saved choice", Shields.ModeFor("example.test", true) == Shields.Mode.Off);

var engine = FilterEngine.Build(new[] { new FilterEngine.Source("Test", "@@||google-analytics.com/allowed.js$script,domain=example.test\n||important.test^$important\n@@||important.test^\n") });
var page = engine.PageFor("https://example.test/");
Check("Built-in tracker still blocked", engine.Match("https://google-analytics.com/collect", ReqType.Xhr, page, true, "google-analytics.com").Blocked);
Check("Targeted allow works for built-in tracker", !engine.Match("https://google-analytics.com/allowed.js", ReqType.Script, page, true, "google-analytics.com").Blocked);
Check("Allow remains scoped to resource type", engine.Match("https://google-analytics.com/allowed.js", ReqType.Xhr, page, true, "google-analytics.com").Blocked);
Check("Allow remains scoped to page", engine.Match("https://google-analytics.com/allowed.js", ReqType.Script, engine.PageFor("https://other.test"), true, "google-analytics.com").Blocked);
Check("Important rule retains priority", engine.Match("https://important.test/a.js", ReqType.Script, page, true, "important.test").Blocked);

async Task Refused(string name, HttpResponseMessage response)
{
    using (response)
    {
        try { await SecurityPolicy.ReadFilterTextAsync(response, default); Check(name, false); }
        catch (Exception ex) when (ex is InvalidDataException or HttpRequestException or DecoderFallbackException) { Check(name, true); }
    }
}
using (var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("valid text", Encoding.UTF8, "text/plain") })
    Check("Plain response read", await SecurityPolicy.ReadFilterTextAsync(response, default) == "valid text");
await Refused("Redirect rejected", new(HttpStatusCode.Found) { Content = new StringContent("redirect") });
await Refused("HTML rejected", new(HttpStatusCode.OK) { Content = new StringContent("error page", Encoding.UTF8, "text/html") });
var oversized = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[1]) };
oversized.Content.Headers.ContentLength = SecurityPolicy.MaxFilterBytes + 1L;
await Refused("Declared oversize rejected before reading", oversized);
await Refused("Streaming oversize rejected", new(HttpStatusCode.OK) { Content = new StreamContent(new GeneratedStream(SecurityPolicy.MaxFilterBytes + 1L)) });
await Refused("Invalid UTF8 rejected", new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 0xC3, 0x28 }) });
using (var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("abc") })
{
    using CancellationTokenSource cancel = new(); cancel.Cancel();
    try { await SecurityPolicy.ReadFilterTextAsync(response, cancel.Token); Check("Cancelled read stops", false); }
    catch (OperationCanceledException) { Check("Cancelled read stops", true); }
}

// Exercise the actual updater on an isolated loopback endpoint. No external list is fetched.
Environment.SetEnvironmentVariable("AVIARY_PROFILE_DIR", AppPaths.Root);
AppSettings.Set("listAds", "true");
foreach (string key in new[] { "listTrackers", "listCookies", "listSocial" }) AppSettings.Set(key, "false");
var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); socket.Start();
int port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
using HttpListener listener = new(); listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
Environment.SetEnvironmentVariable("AVIARY_TEST_FILTER_BASE", $"http://127.0.0.1:{port}");
string list = "[Adblock Plus 2.0]\n! Title: EasyList\n! Last modified: 21 Oct 2026 00:00 UTC\n" + string.Concat(Enumerable.Repeat("||tracking.example.test^\n", 1100));
var server = Task.Run(async () => {
    var request = await listener.GetContextAsync();
    Check("Updater sends no cookie", string.IsNullOrEmpty(request.Request.Headers["Cookie"]));
    request.Response.ContentType = "text/plain";
    byte[] body = Encoding.UTF8.GetBytes(list); request.Response.ContentLength64 = body.Length;
    await request.Response.OutputStream.WriteAsync(body); request.Response.Close();
});
var update = await FilterLists.UpdateAsync("security-test"); await server;
Check("Actual updater accepts valid list", update.Ok && update.Changed && FilterLists.Engine is not null);
string saved = File.ReadAllText(Path.Combine(FilterLists.UpdatedFolder, "easylist.txt"));
var redirectServer = Task.Run(async () => {
    var request = await listener.GetContextAsync(); request.Response.StatusCode = 302;
    request.Response.RedirectLocation = "http://example.invalid/untrusted"; request.Response.Close();
});
update = await FilterLists.UpdateAsync("redirect-test"); await redirectServer;
Check("All enabled updates failing reports failure", !update.Ok && !update.Changed);
Check("Rejected redirect preserves known list", File.ReadAllText(Path.Combine(FilterLists.UpdatedFolder, "easylist.txt")) == saved);
listener.Stop();
Check("Policy covers future separate sharing consent", PrivacyPolicy.Markdown().Contains("Local recording consent is not consent to send data."));
Check("Policy discloses Microsoft services", PrivacyPolicy.Markdown().Contains("SmartScreen"));
string policyPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../PRIVACY.md"));
Check("Published policy matches Settings source", File.Exists(policyPath)
    && File.ReadAllText(policyPath).Replace("\r\n", "\n") == PrivacyPolicy.Markdown());
Console.WriteLine($"Security: {passed} passed, {failed} failed.");
Environment.ExitCode = failed == 0 ? 0 : 1;

sealed class GeneratedStream(long remaining) : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => 0; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = (int)Math.Min(count, remaining); Array.Fill(buffer, (byte)'a', offset, read); remaining -= read; return read;
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
