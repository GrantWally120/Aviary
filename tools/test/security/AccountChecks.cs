using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aviary;

/// <summary>
/// What has to be true before accounts are built: the policy on Aviary's own pages, the token
/// vault, the sign-in primitives and the scrubber's key list. None of this is wired to a screen.
/// </summary>
internal static class AccountChecks
{
    internal static async Task RunAsync(Action<string, bool> check)
    {
        Csp(check);
        Vault(check);
        OAuth(check);
        await Loopback(check);
        Scrub(check);
    }

    // ── Content-Security-Policy of the internal pages ───────────────────────

    // An on…= attribute written in markup, or inside a JS string that builds markup ('onerror="…" or " onerror=…").
    private const string InlineHandler = "[\\s'\"]on(?!ly)[a-z]{3,}\\s*=\\s*(\\\\?[\"']|&quot;)";

    private static void Csp(Action<string, bool> check)
    {
        check("The inline-handler check recognises the forms the Start Page used to have",
            Regex.IsMatch("x + 'onerror=\"this.remove()\">'", InlineHandler) && Regex.IsMatch("<img src=a onerror=\"y\">", InlineHandler)
            && Regex.IsMatch("<button onclick='y'>", InlineHandler) && !Regex.IsMatch("probe.onload = function(){}", InlineHandler));

        string nonceA = PageCsp.NewNonce(), nonceB = PageCsp.NewNonce();
        check("CSP nonce is 24 URL-safe-or-base64 characters", nonceA.Length == 24 && Regex.IsMatch(nonceA, "^[A-Za-z0-9+/]{24}$"));
        check("CSP nonces differ between builds", nonceA != nonceB);

        foreach ((string label, string template, PageCsp.Page page) in new[]
        {
            ("Start Page", StartPage.Html, PageCsp.Page.Start),
            ("Settings", InternalPages.Settings, PageCsp.Page.Settings),
        })
        {
            string html = PageCsp.Apply(template, page), again = PageCsp.Apply(template, page);
            Match meta = Regex.Match(html, "<meta http-equiv=\"Content-Security-Policy\" content=\"([^\"]*)\">");
            string policy = meta.Groups[1].Value;
            Match script = Regex.Match(html, "<script nonce=\"([^\"]+)\">");
            check($"{label}: exactly one policy tag, before the script", Regex.Matches(html, "Content-Security-Policy").Count == 1 && meta.Success && script.Success && meta.Index < script.Index);
            check($"{label}: the policy names the script's nonce", script.Success && policy.Contains($"script-src 'nonce-{script.Groups[1].Value}';"));
            check($"{label}: no marker is left in the page", !html.Contains("__AVIARY_NONCE__") && !html.Contains("__AVIARY_CSP__"));
            check($"{label}: a new nonce for every build", Regex.Match(again, "<script nonce=\"([^\"]+)\">").Groups[1].Value != script.Groups[1].Value);
            check($"{label}: scripts cannot be inline or eval'd", !Regex.IsMatch(policy, @"script-src[^;]*'unsafe-(inline|eval)'") && !policy.Contains("unsafe-eval") && !policy.Contains("wasm-unsafe-eval"));
            check($"{label}: default is none; no plugins, framing, forms, base or connections",
                policy.StartsWith("default-src 'none';") && policy.Contains("object-src 'none'") && policy.Contains("frame-src 'none'")
                && policy.Contains("form-action 'none'") && policy.Contains("base-uri 'none'") && policy.Contains("connect-src 'none'"));
            check($"{label}: pictures only from where the page draws them",
                page == PageCsp.Page.Start
                    ? policy.Contains("img-src data: https://aviary.localhost https://collection.aviary https://menagerie.aviary;") && !Regex.IsMatch(policy, @"img-src[^;]*(\*|https:\s|http:)")
                    : policy.Contains("img-src data:;"));
            check($"{label}: one script block, no inline event handler, no javascript: link, no eval",
                Regex.Matches(template, "<script").Count == 1
                && !Regex.IsMatch(template, InlineHandler)
                && !template.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
                && !Regex.IsMatch(template, @"\beval\s*\(|new\s+Function\s*\(|(setTimeout|setInterval)\s*\(\s*['""`]|document\.write\s*\("));
        }

        string Throws(string template) { try { PageCsp.Apply(template, PageCsp.Page.Settings); return "none"; } catch (InvalidOperationException) { return "refused"; } }
        check("Template without the policy marker is refused", Throws("<head></head><script nonce=\"__AVIARY_NONCE__\"></script>") == "refused");
        check("Template whose script lacks the nonce is refused", Throws("<head><!--__AVIARY_CSP__--></head><script></script>") == "refused");
        check("Template with a second script is refused", Throws("<head><!--__AVIARY_CSP__--></head><script nonce=\"__AVIARY_NONCE__\"></script><script></script>") == "refused");
        check("Template with the policy after the script is refused", Throws("<script nonce=\"__AVIARY_NONCE__\"></script><!--__AVIARY_CSP__-->") == "refused");
        check("Template with a duplicated marker is refused", Throws("<!--__AVIARY_CSP__--><!--__AVIARY_CSP__--><script nonce=\"__AVIARY_NONCE__\"></script>") == "refused");

        // Page data is written into the script block as JSON; that must never be able to close the block.
        string hostile = JsonSerializer.Serialize(new { a = "</script><script>alert(1)</script>", b = "<!--", c = ((char)0x2028).ToString() });
        check("JSON written into a page cannot close its script block", !hostile.Contains("</", StringComparison.Ordinal) && !hostile.Contains("<!--", StringComparison.Ordinal));
    }

    // ── The token vault ──────────────────────────────────────────────────────

    private static void Vault(Action<string, bool> check)
    {
        string folder = Path.Combine(Path.GetTempPath(), "aviary-vault-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            TokenVault vault = new(folder);
            foreach (string good in new[] { "google.refresh", "a", "id-token_1", new string('a', 64) })
                check("Vault accepts name " + good[..Math.Min(good.Length, 20)], TokenVault.IsValidName(good));
            foreach (string bad in new[] { "", "../x", "..\\x", "A", "a b", ".hidden", "con:", "a/b", new string('a', 65), "名前" })
                check("Vault refuses name " + bad.Replace("\\", "\\\\"), !TokenVault.IsValidName(bad) && !vault.Set(bad, "x") && vault.Get(bad) is null);

            string secret = "ya29.A0-secret_value/with+odd=chars-ünï";
            check("Vault stores and returns a secret", vault.Set("google.refresh", secret) && vault.Get("google.refresh") == secret);
            byte[] file = File.ReadAllBytes(Path.Combine(folder, "google.refresh.tok"));
            check("Vault file does not contain the secret in any common encoding",
                !Contains(file, Encoding.UTF8.GetBytes(secret)) && !Contains(file, Encoding.Unicode.GetBytes(secret)) && !Contains(file, Encoding.UTF8.GetBytes("ya29")));
            check("Vault file is marked and sealed", file.Length > 40 && file[0] == 'A' && file[1] == 'V' && file[2] == 'T' && file[3] == '1');
            check("Vault replaces a secret and leaves no temporary file", vault.Set("google.refresh", "second") && vault.Get("google.refresh") == "second"
                && Directory.GetFiles(folder).All(f => f.EndsWith(".tok")));
            check("Vault returns null for a missing secret", vault.Get("absent") is null);

            vault.Set("a", "one"); vault.Set("b", "two");
            File.Copy(Path.Combine(folder, "a.tok"), Path.Combine(folder, "c.tok"));
            check("A secret copied under another name does not open", vault.Get("c") is null);
            check("Names lists only valid secrets, sorted", string.Join(",", vault.Names()) == "a,b,c,google.refresh");
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "x"); File.WriteAllText(Path.Combine(folder, "Bad Name.tok"), "x");
            check("Names ignores stray files", string.Join(",", vault.Names()) == "a,b,c,google.refresh");

            byte[] altered = File.ReadAllBytes(Path.Combine(folder, "a.tok")); altered[^3] ^= 0x5A;
            File.WriteAllBytes(Path.Combine(folder, "a.tok"), altered);
            check("A tampered secret returns null instead of throwing", vault.Get("a") is null);
            File.WriteAllBytes(Path.Combine(folder, "a.tok"), altered[..10]);
            check("A truncated secret returns null", vault.Get("a") is null);
            File.WriteAllBytes(Path.Combine(folder, "a.tok"), new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            check("A foreign file returns null", vault.Get("a") is null);

            check("Vault takes a 16 KiB secret", vault.Set("big", new string('x', TokenVault.MaxSecretBytes)) && vault.Get("big")!.Length == TokenVault.MaxSecretBytes);
            check("Vault refuses a secret over 16 KiB and keeps the old one", !vault.Set("big", new string('x', TokenVault.MaxSecretBytes + 1)) && vault.Get("big")!.Length == TokenVault.MaxSecretBytes);
            check("Vault refuses an empty secret", !vault.Set("empty", "") && vault.Get("empty") is null);

            check("Vault deletes a secret", vault.Delete("b") && vault.Get("b") is null && !File.Exists(Path.Combine(folder, "b.tok")));
            check("Deleting an absent secret says so", !vault.Delete("b"));

            int existing = vault.Names().Count;
            for (int i = existing; i < TokenVault.MaxEntries; i++) { vault.Set("fill" + i, "x"); }
            check("Vault stops at its entry limit", !vault.Set("one-too-many", "x") && vault.Get("one-too-many") is null);
            check("Vault still replaces an existing secret at the limit", vault.Set("fill" + existing, "again") && vault.Get("fill" + existing) == "again");

            int removed = vault.Wipe();
            check("Wipe removes every secret", removed >= TokenVault.MaxEntries - 3 && vault.Names().Count == 0);
            File.Delete(Path.Combine(folder, "notes.txt")); File.Delete(Path.Combine(folder, "Bad Name.tok"));
            vault.Wipe();
            check("Wipe removes the folder once it is empty", !Directory.Exists(folder));

            TokenVault failing = new(folder, new ThrowingProtector());
            check("A protector that fails makes Set fail quietly and Get return null", !failing.Set("x", "y") && failing.Get("x") is null);
            Directory.Delete(folder, true);

            string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../../src/Aviary/TokenVault.cs"));
            check("Vault source never logs", !Regex.IsMatch(text, @"Diagnostics\.|Console\.|Debug\.|Trace\."));
        }
        finally { try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch (IOException) { } }
    }

    private sealed class ThrowingProtector : ISecretProtector
    {
        public byte[] Protect(byte[] plain, byte[] entropy) => throw new CryptographicException("no");
        public byte[] Unprotect(byte[] sealedBytes, byte[] entropy) => throw new CryptographicException("no");
    }

    private static bool Contains(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;

    // ── Sign-in primitives ───────────────────────────────────────────────────

    private static void OAuth(Action<string, bool> check)
    {
        // RFC 7636 appendix B
        check("PKCE challenge matches the RFC 7636 test vector",
            OAuthFlow.CodeChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk") == "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM");
        string verifier = OAuthFlow.NewCodeVerifier();
        check("Code verifier is 43 valid characters and differs each time", verifier.Length == 43 && OAuthFlow.IsValidVerifier(verifier) && verifier != OAuthFlow.NewCodeVerifier());
        check("State and nonce are 32 URL-safe characters", Regex.IsMatch(OAuthFlow.NewState(), "^[A-Za-z0-9_-]{32}$") && Regex.IsMatch(OAuthFlow.NewNonce(), "^[A-Za-z0-9_-]{32}$"));
        foreach (string bad in new[] { "", new string('a', 42), new string('a', 129), new string('a', 42) + "!", new string('a', 42) + " " })
            check("Verifier refused: length " + bad.Length, !OAuthFlow.IsValidVerifier(bad));
        check("Challenge refuses a weak verifier", Throws(() => OAuthFlow.CodeChallenge("short")));

        Uri auth = new("https://accounts.example.test/o/oauth2/v2/auth"), redirect = new("http://127.0.0.1:53682/callback");
        string state = OAuthFlow.NewState(), challenge = OAuthFlow.CodeChallenge(verifier), nonce = OAuthFlow.NewNonce();
        Uri built = OAuthFlow.BuildAuthorizationUri(auth, "client-123.apps.example", redirect, new[] { "openid", "email" }, state, challenge, nonce);
        string q = built.Query;
        check("Authorization URL carries code flow, S256, state, nonce and the encoded redirect",
            built.Host == "accounts.example.test" && q.Contains("response_type=code") && q.Contains("code_challenge_method=S256") && q.Contains("code_challenge=" + challenge)
            && q.Contains("state=" + state) && q.Contains("nonce=" + nonce) && q.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A53682%2Fcallback") && q.Contains("scope=openid%20email"));
        check("Authorization URL never carries a secret or the verifier", !q.Contains("secret") && !q.Contains(verifier));
        check("Authorization URL without a nonce omits it", !OAuthFlow.BuildAuthorizationUri(auth, "c", redirect, new[] { "openid" }, state, challenge).Query.Contains("nonce"));
        check("IPv6 loopback redirect is accepted", OAuthFlow.IsValidRedirect(new Uri("http://[::1]:5000/cb")));
        foreach (string badRedirect in new[] { "http://localhost:5000/cb", "http://192.168.1.5:5000/cb", "https://127.0.0.1:5000/cb", "http://127.0.0.1/cb", "http://127.0.0.1:5000/cb?x=1", "http://127.0.0.1:5000/", "http://user@127.0.0.1:5000/cb", "http://127.0.0.1:5000/cb#f", "http://example.test:5000/cb" })
            check("Redirect refused: " + badRedirect, !OAuthFlow.IsValidRedirect(new Uri(badRedirect)));
        check("Plain-http authorization endpoint refused", Throws(() => OAuthFlow.BuildAuthorizationUri(new Uri("http://accounts.example.test/auth"), "c", redirect, new[] { "openid" }, state, challenge)));
        check("Authorization endpoint with credentials refused", Throws(() => OAuthFlow.BuildAuthorizationUri(new Uri("https://u:p@accounts.example.test/auth"), "c", redirect, new[] { "openid" }, state, challenge)));
        check("Scope that smuggles a parameter refused", Throws(() => OAuthFlow.BuildAuthorizationUri(auth, "c", redirect, new[] { "openid&prompt=none" }, state, challenge)));
        check("Weak state refused", Throws(() => OAuthFlow.BuildAuthorizationUri(auth, "c", redirect, new[] { "openid" }, "abc", challenge)));
        check("Client id that smuggles a parameter refused", Throws(() => OAuthFlow.BuildAuthorizationUri(auth, "c&x=1", redirect, new[] { "openid" }, state, challenge)));

        string S = "STATE_0123456789abcdef_STATE";
        OAuthFlow.CallbackResult ok = OAuthFlow.ParseCallback("/callback?code=4%2F0AbCd&state=" + S, "/callback", S);
        check("Callback with the right state yields the decoded code", ok is { Ok: true, Authentic: true, Code: "4/0AbCd" });
        check("Callback with another state is refused and not authentic", OAuthFlow.ParseCallback("/callback?code=x&state=OTHER", "/callback", S) is { Ok: false, Authentic: false, Error: "state_mismatch" });
        check("Callback without a state is refused", OAuthFlow.ParseCallback("/callback?code=x", "/callback", S) is { Ok: false, Authentic: false });
        check("Callback on another path is refused", OAuthFlow.ParseCallback("/other?code=x&state=" + S, "/callback", S) is { Ok: false, Authentic: false });
        check("Repeated code parameter is refused", !OAuthFlow.ParseCallback("/callback?code=a&code=b&state=" + S, "/callback", S).Ok);
        check("Repeated state parameter is refused", !OAuthFlow.ParseCallback("/callback?state=" + S + "&state=" + S + "&code=a", "/callback", S).Ok);
        check("Provider error with our state is authentic and carries the plain code", OAuthFlow.ParseCallback("/callback?error=access_denied&state=" + S, "/callback", S) is { Ok: false, Authentic: true, Error: "access_denied" });
        check("Provider error text is never echoed unless a plain word", OAuthFlow.ParseCallback("/callback?error=%3Cscript%3E&state=" + S, "/callback", S) is { Authentic: true, Error: "provider_error" });
        check("Provider error with another state is not authentic", OAuthFlow.ParseCallback("/callback?error=access_denied&state=nope", "/callback", S) is { Authentic: false });
        check("Callback without code or error is refused", OAuthFlow.ParseCallback("/callback?state=" + S, "/callback", S) is { Ok: false, Authentic: true, Error: "invalid_code" });
        check("Code with a space or control character is refused", !OAuthFlow.ParseCallback("/callback?code=a%20b&state=" + S, "/callback", S).Ok && !OAuthFlow.ParseCallback("/callback?code=a%00b&state=" + S, "/callback", S).Ok);
        check("Overlong code is refused", !OAuthFlow.ParseCallback("/callback?code=" + new string('a', 2049) + "&state=" + S, "/callback", S).Ok);
        check("Overlong query is refused", !OAuthFlow.ParseCallback("/callback?x=" + new string('a', 5000) + "&code=a&state=" + S, "/callback", S).Ok);
        check("Same-length wrong state is refused", !OAuthFlow.ParseCallback("/callback?code=a&state=" + S.Replace('S', 'X'), "/callback", S).Ok);

        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        TimeSpan skew = TimeSpan.FromMinutes(2);
        string claims(string? iss = "https://accounts.example.test", string aud = "\"client-1\"", long exp = 1_800_003_600, long iat = 1_800_000_000, string? nonceValue = "N0NCE0123456789abcdef", string? sub = "1234567890", string extra = "")
        {
            List<string> parts = new() { $"\"aud\":{aud}", $"\"exp\":{exp}", $"\"iat\":{iat}" };
            if (iss is not null) parts.Add($"\"iss\":\"{iss}\"");
            if (nonceValue is not null) parts.Add($"\"nonce\":\"{nonceValue}\"");
            if (sub is not null) parts.Add($"\"sub\":\"{sub}\"");
            return "{" + string.Join(",", parts) + extra + "}";
        }
        string? Check(string json) => OAuthFlow.CheckIdTokenClaims(json, "https://accounts.example.test", "client-1", "N0NCE0123456789abcdef", now, skew);
        check("Valid ID token claims pass", Check(claims()) is null);
        check("Wrong issuer fails", Check(claims(iss: "https://evil.test")) == "issuer");
        check("Missing issuer fails", Check(claims(iss: null)) == "issuer");
        check("Wrong audience fails", Check(claims(aud: "\"other\"")) == "audience");
        check("Audience list containing ours passes with azp", Check(claims(aud: "[\"client-1\",\"other\"]", extra: ",\"azp\":\"client-1\"")) is null);
        check("Audience list without azp fails", Check(claims(aud: "[\"client-1\",\"other\"]")) == "authorized_party");
        check("Audience list without ours fails", Check(claims(aud: "[\"a\",\"b\"]")) == "audience");
        check("Expired token fails", Check(claims(exp: 1_800_000_000 - 600)) == "expired");
        check("Expiry within the clock skew passes", Check(claims(exp: 1_800_000_000 - 60)) is null);
        check("Token issued in the future fails", Check(claims(iat: 1_800_000_000 + 600)) == "issued_in_future");
        check("Wrong nonce fails", Check(claims(nonceValue: "other")) == "nonce");
        check("Missing nonce fails", Check(claims(nonceValue: null)) == "nonce");
        check("Missing subject fails", Check(claims(sub: null)) == "subject");
        check("Absurd expiry does not throw", Check(claims(exp: long.MaxValue)) is null && Check(claims(exp: long.MinValue)) == "expired");
        check("Non-object payload fails", Check("[1,2]") == "payload_shape" && Check("not json") == "payload_shape" && Check("") == "payload_size");
        check("Oversize payload fails", Check("{\"x\":\"" + new string('a', 9000) + "\"}") == "payload_size");

        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"sub\":\"1\"}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        check("JWT payload decodes", OAuthFlow.DecodeJwtPayload("e30." + payload + ".sig") == "{\"sub\":\"1\"}");
        check("Malformed JWT is refused", OAuthFlow.DecodeJwtPayload("a.b") is null && OAuthFlow.DecodeJwtPayload("a.!!!.c") is null && OAuthFlow.DecodeJwtPayload("") is null);
        check("Oversize JWT is refused", OAuthFlow.DecodeJwtPayload("a." + new string('A', 20000) + ".c") is null);
        check("Secrets compare without length leaks to the caller", OAuthFlow.SameSecret("abc", "abc") && !OAuthFlow.SameSecret("abc", "abd") && !OAuthFlow.SameSecret("abc", "abcd") && !OAuthFlow.SameSecret(null, "abc"));
    }

    private static bool Throws(Action action) { try { action(); return false; } catch (ArgumentException) { return true; } }

    // ── The loopback receiver ────────────────────────────────────────────────

    private static async Task<(int Status, string Head, string Body)> Send(int port, string request)
    {
        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, port);
        NetworkStream stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));
        using MemoryStream all = new();
        byte[] buffer = new byte[4096];
        using CancellationTokenSource limit = new(TimeSpan.FromSeconds(5));
        try { int n; while ((n = await stream.ReadAsync(buffer, limit.Token)) > 0) all.Write(buffer, 0, n); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
        string text = Encoding.UTF8.GetString(all.ToArray());
        int split = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        string head = split < 0 ? text : text[..split], body = split < 0 ? string.Empty : text[(split + 4)..];
        return (head.Length > 12 && int.TryParse(head.AsSpan(9, 3), out int status) ? status : 0, head, body);
    }

    private static async Task Loopback(Action<string, bool> check)
    {
        string state = OAuthFlow.NewState();
        string get(int port, string target, string? host = null) => $"GET {target} HTTP/1.1\r\nHost: {host ?? "127.0.0.1:" + port}\r\nConnection: close\r\n\r\n";

        using (LoopbackReceiver receiver = LoopbackReceiver.Start("/callback", state))
        {
            int port = receiver.Port;
            check("Receiver picks a port and a loopback redirect", port > 0 && receiver.RedirectUri.ToString() == $"http://127.0.0.1:{port}/callback" && OAuthFlow.IsValidRedirect(receiver.RedirectUri));
            Task<OAuthFlow.CallbackResult> waiting = receiver.WaitAsync(TimeSpan.FromSeconds(20));

            var favicon = await Send(port, get(port, "/favicon.ico"));
            check("A favicon request is turned away", favicon.Status == 404);
            var rebinding = await Send(port, get(port, $"/callback?code=STOLEN&state={state}", "attacker.example:" + port));
            check("A request with another Host header (DNS rebinding) is refused", rebinding.Status == 400 && !rebinding.Body.Contains("STOLEN"));
            var post = await Send(port, $"POST /callback?code=STOLEN&state={state} HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nContent-Length: 0\r\n\r\n");
            check("A POST is refused", post.Status == 400);
            var wrong = await Send(port, get(port, "/callback?code=FORGED&state=" + new string('z', 32)));
            check("A callback with another state is refused", wrong.Status == 400 && !wrong.Body.Contains("FORGED"));
            var oversize = await Send(port, "GET /callback HTTP/1.1\r\nHost: 127.0.0.1:" + port + "\r\nX-Pad: " + new string('a', 9000) + "\r\n\r\n");
            check("An oversize request is refused", oversize.Status == 431 || oversize.Status == 0);
            check("None of that ended the sign-in", !waiting.IsCompleted);

            var good = await Send(port, get(port, $"/callback?code=GOOD_CODE&state={state}"));
            OAuthFlow.CallbackResult result = await waiting;
            check("The genuine callback completes it with the code", result is { Ok: true, Code: "GOOD_CODE" });
            check("The browser is answered 200 with a fixed page", good.Status == 200 && good.Body.Contains("signed in"));
            check("The answer never repeats the code or state", !good.Body.Contains("GOOD_CODE") && !good.Body.Contains(state) && !good.Head.Contains("GOOD_CODE"));
            check("The answer is not cacheable, sends no referrer and carries a policy",
                good.Head.Contains("Cache-Control: no-store") && good.Head.Contains("Referrer-Policy: no-referrer") && good.Head.Contains("Content-Security-Policy: default-src 'none'")
                && !good.Head.Contains("Set-Cookie") && good.Body.Contains("history.replaceState"));
        }

        using (LoopbackReceiver receiver = LoopbackReceiver.Start("/cb", state))
        {
            Task<OAuthFlow.CallbackResult> waiting = receiver.WaitAsync(TimeSpan.FromSeconds(20));
            await Send(receiver.Port, get(receiver.Port, "/cb?error=access_denied&state=" + state));
            check("A provider error with our state ends the sign-in as that error", await waiting is { Ok: false, Error: "access_denied", Authentic: true });
        }

        using (LoopbackReceiver receiver = LoopbackReceiver.Start("/cb", state))
        {
            Task<OAuthFlow.CallbackResult> waiting = receiver.WaitAsync(TimeSpan.FromSeconds(20));
            for (int i = 0; i < 3; i++) await Send(receiver.Port, get(receiver.Port, "/cb?code=x&state=" + new string('q', 32)));
            check("Three forged callbacks end the sign-in without a code", await waiting is { Ok: false, Code: null, Error: "state_mismatch" });
        }

        using (LoopbackReceiver receiver = LoopbackReceiver.Start("/cb", state))
        {
            OAuthFlow.CallbackResult result = await receiver.WaitAsync(TimeSpan.FromMilliseconds(300));
            check("No callback in time ends as a timeout", result is { Ok: false, Error: "timeout" });
        }

        using (LoopbackReceiver receiver = LoopbackReceiver.Start("/cb", state))
        {
            using CancellationTokenSource cancel = new();
            Task<OAuthFlow.CallbackResult> waiting = receiver.WaitAsync(TimeSpan.FromSeconds(20), cancel.Token);
            cancel.Cancel();
            check("Cancelling ends the wait", await waiting is { Ok: false, Error: "cancelled" });
        }

        using (LoopbackReceiver receiver = LoopbackReceiver.Start("/cb", state))
        {
            IPAddress? lan = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (lan is null) { check("Receiver unreachable from the network (skipped: no network address)", true); }
            else
            {
                using TcpClient other = new();
                using CancellationTokenSource limit = new(TimeSpan.FromSeconds(2));
                bool reached;
                try { await other.ConnectAsync(lan, receiver.Port, limit.Token); reached = true; } catch (Exception ex) when (ex is SocketException or OperationCanceledException) { reached = false; }
                check("Receiver does not answer on the machine's network address", !reached);
            }
        }

        check("Receiver refuses a state that is too short", Throws(() => LoopbackReceiver.Start("/cb", "short")));
        check("Receiver refuses a callback path with a query or no slash", Throws(() => LoopbackReceiver.Start("cb", state)) && Throws(() => LoopbackReceiver.Start("/cb?x=1", state)));
    }

    // ── What the diagnostics scrubber masks ──────────────────────────────────

    private static void Scrub(Action<string, bool> check)
    {
        void Masked(string name, string value, string secret) => check("Scrub: " + name, !Diagnostics.Scrub(value).Contains(secret, StringComparison.Ordinal));
        foreach ((string key, string label) in new[]
        {
            ("passphrase", "passphrase"), ("device_code", "device code"), ("user_code", "user code"), ("client_assertion", "client assertion"),
            ("security_code", "security code"), ("recovery_code", "recovery code"), ("backup_code", "backup code"), ("totp", "TOTP"), ("mfa_code", "MFA code"),
            ("seed", "seed"), ("mnemonic", "mnemonic"), ("cvv", "CVV"), ("cvc", "CVC"), ("bearer", "bearer"), ("x-api-key", "x-api-key"),
            ("clientSecret", "camelCase client secret"), ("refreshToken", "camelCase refresh token"), ("idToken", "camelCase id token"),
            ("privateKey", "camelCase private key"), ("recoveryCode", "camelCase recovery code"), ("deviceCode", "camelCase device code"),
        })
        {
            Masked(label + " in a query", "https://example.test/path?" + key + "=SECRETVALUE_" + key + "&next=home", "SECRETVALUE_" + key);
            Masked(label + " in JSON", "{\"" + key + "\":\"SECRETVALUE_" + key + "\",\"message\":\"kept\"}", "SECRETVALUE_" + key);
        }

        Masked("form-encoded sign-in body", "grant_type=authorization_code&code=SECRET_AUTH_CODE&code_verifier=SECRET_VERIFIER_2&redirect_uri=http%3A%2F%2F127.0.0.1%3A5000%2Fcb&client_secret=SECRET_CS", "SECRET_AUTH_CODE");
        Masked("form-encoded sign-in body verifier", "grant_type=authorization_code&code=SECRET_AUTH_CODE&code_verifier=SECRET_VERIFIER_2", "SECRET_VERIFIER_2");
        Masked("loopback callback address", "GET http://127.0.0.1:53682/callback?code=4%2F0SECRET_CB&state=SECRET_ST HTTP/1.1", "4%2F0SECRET_CB");
        Masked("Proxy-Authorization header", "Proxy-Authorization: Basic c2VjcmV0OnNlY3JldA==", "c2VjcmV0OnNlY3JldA");
        Masked("X-Auth-Token header", "X-Auth-Token: SECRET_XAT_0123456789", "SECRET_XAT_0123456789");

        foreach (string ordinary in new[]
        {
            "https://example.test/?q=birds&keyword=owl&monkey=1&design=2&zipcode=1234&stateful=1&session_count=3",
            "{\"message\":\"signed in\",\"count\":3,\"account\":\"shown\"}",
        })
        {
            string scrubbed = Diagnostics.Scrub(ordinary);
            check("Scrub keeps ordinary text: " + ordinary[..24], scrubbed.Contains("keyword=owl") || scrubbed.Contains("signed in"));
        }
    }
}
