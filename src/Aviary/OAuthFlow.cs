using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Aviary;

/// <summary>
/// The parts of "Sign in with Google" (or any OpenID Connect provider) that must be right
/// before an account system is built on them: authorization code flow with S256 PKCE, a fresh
/// state and nonce, a loopback callback that answers only the one request it expects, and the
/// checks on an ID token's claims. Design and the reasons are in docs/SECURITY.md.
///
/// The sign-in page is opened in the person's own browser (never inside a WebView2 Aviary
/// controls), the code comes back here over 127.0.0.1, and the token request goes straight to the
/// provider over TLS. Because the ID token then comes from that TLS exchange and not from a
/// redirect, OpenID Connect Core 3.1.3.7 allows the claims check below without verifying the
/// token's signature; anything that accepts an ID token from elsewhere must verify it first.
///
/// Not wired to any screen yet, and holds no client id or secret: those belong to whoever
/// registers the application.
/// </summary>
internal static class OAuthFlow
{
    public const int MaxCodeChars = 2048;
    public const int MaxQueryChars = 4096;
    public const int MaxIdTokenPayloadBytes = 8192;

    // ── PKCE (RFC 7636) and the random values ───────────────────────────────

    /// <summary>43 characters from 32 random bytes: the shortest verifier the RFC allows, with full entropy.</summary>
    public static string NewCodeVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string CodeChallenge(string verifier)
    {
        if (!IsValidVerifier(verifier)) { throw new ArgumentException("Not a valid PKCE code verifier."); }
        return Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    public static bool IsValidVerifier(string? value)
    {
        if (value is null || value.Length < 43 || value.Length > 128) { return false; }
        foreach (char c in value)
        {
            bool unreserved = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c is '-' or '.' or '_' or '~';
            if (!unreserved) { return false; }
        }

        return true;
    }

    /// <summary>A CSRF state or an OIDC nonce: 24 random bytes, 32 URL-safe characters.</summary>
    public static string NewState() => Base64Url(RandomNumberGenerator.GetBytes(24));

    public static string NewNonce() => Base64Url(RandomNumberGenerator.GetBytes(24));

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // ── The authorization request ───────────────────────────────────────────

    /// <summary>127.0.0.1 or [::1], an explicit port, no userinfo, query or fragment, plain http (RFC 8252 7.3).</summary>
    public static bool IsValidRedirect(Uri? redirect) =>
        redirect is { IsAbsoluteUri: true, Scheme: "http", IsDefaultPort: false }
        && redirect.UserInfo.Length == 0 && redirect.Query.Length == 0 && redirect.Fragment.Length == 0
        && redirect.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
        && IPAddress.TryParse(redirect.Host.Trim('[', ']'), out IPAddress? ip) && IPAddress.IsLoopback(ip)
        && redirect.AbsolutePath.Length > 1 && redirect.Port is > 0 and <= 65535;

    public static Uri BuildAuthorizationUri(Uri endpoint, string clientId, Uri redirect, IEnumerable<string> scopes,
        string state, string codeChallenge, string? nonce = null)
    {
        if (endpoint is not { IsAbsoluteUri: true, Scheme: "https" } || endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0)
            throw new ArgumentException("The authorization endpoint must be an https address.");
        if (!IsValidRedirect(redirect)) throw new ArgumentException("The redirect must be a loopback address with a port.");
        if (!IsToken(clientId, 1, 256)) throw new ArgumentException("Invalid client id.");
        if (!IsToken(state, 16, 128) || !IsToken(codeChallenge, 43, 128)) throw new ArgumentException("Invalid state or challenge.");
        if (nonce is not null && !IsToken(nonce, 16, 128)) throw new ArgumentException("Invalid nonce.");
        string[] scopeList = scopes.ToArray();
        if (scopeList.Length == 0 || scopeList.Any(s => !IsToken(s, 1, 128, allowScopeChars: true)))
            throw new ArgumentException("Invalid scope.");

        List<string> query = new()
        {
            "response_type=code",
            "client_id=" + Uri.EscapeDataString(clientId),
            "redirect_uri=" + Uri.EscapeDataString(redirect.AbsoluteUri),
            "scope=" + Uri.EscapeDataString(string.Join(' ', scopeList)),
            "state=" + state,
            "code_challenge=" + codeChallenge,
            "code_challenge_method=S256",
        };
        if (nonce is not null) { query.Add("nonce=" + nonce); }

        UriBuilder builder = new(endpoint) { Query = string.Join('&', query) };
        return builder.Uri;
    }

    // ── The callback ────────────────────────────────────────────────────────

    /// <param name="Authentic">
    /// The request carried our state, so it is the provider's answer to OUR request, whatever it said.
    /// A request that is not authentic is noise (or an attack) and must never end a sign-in.
    /// </param>
    public readonly record struct CallbackResult(bool Ok, string? Code, string? Error, bool Authentic = false)
    {
        public static CallbackResult Fail(string error, bool authentic = false) => new(false, null, error, authentic);
    }

    /// <summary>
    /// The request target the browser sent to the loopback address ("/cb?code=…&amp;state=…").
    /// Anything unexpected is a failure; nothing from the request is echoed back except an error
    /// code the provider sent, and only when it is a plain lowercase word.
    /// </summary>
    public static CallbackResult ParseCallback(string requestTarget, string expectedPath, string expectedState)
    {
        if (string.IsNullOrEmpty(requestTarget) || requestTarget.Length > MaxQueryChars + 256) { return CallbackResult.Fail("invalid_request"); }
        int q = requestTarget.IndexOf('?');
        string path = q < 0 ? requestTarget : requestTarget[..q], query = q < 0 ? string.Empty : requestTarget[(q + 1)..];
        if (!string.Equals(path, expectedPath, StringComparison.Ordinal)) { return CallbackResult.Fail("wrong_path"); }
        if (query.Length == 0 || query.Length > MaxQueryChars) { return CallbackResult.Fail("invalid_request"); }

        Dictionary<string, string> values = new(StringComparer.Ordinal);
        foreach (string pair in query.Split('&'))
        {
            if (pair.Length == 0) { continue; }
            int eq = pair.IndexOf('=');
            string key, value;
            try
            {
                key = Uri.UnescapeDataString(eq < 0 ? pair : pair[..eq]);
                value = eq < 0 ? string.Empty : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            }
            catch (UriFormatException) { return CallbackResult.Fail("invalid_request"); }

            // A repeated parameter is how a request gets one meaning for a filter and another for the app.
            if (!values.TryAdd(key, value)) { return CallbackResult.Fail("invalid_request"); }
        }

        if (!values.TryGetValue("state", out string? state) || !SameSecret(state, expectedState)) { return CallbackResult.Fail("state_mismatch"); }
        if (values.TryGetValue("error", out string? error))
        {
            return CallbackResult.Fail(error.Length is > 0 and <= 40 && error.All(c => (c >= 'a' && c <= 'z') || c == '_') ? error : "provider_error", authentic: true);
        }

        if (!values.TryGetValue("code", out string? code) || code.Length is 0 or > MaxCodeChars || code.Any(c => c <= ' ' || c > '~'))
        {
            return CallbackResult.Fail("invalid_code", authentic: true);
        }

        return new CallbackResult(true, code, null, true);
    }

    /// <summary>Compares two secrets without stopping at the first difference.</summary>
    public static bool SameSecret(string? a, string? b) =>
        a is not null && b is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    // ── The ID token's claims ───────────────────────────────────────────────

    /// <returns>null when the claims are acceptable, otherwise a short fixed reason (never a claim's value).</returns>
    public static string? CheckIdTokenClaims(string payloadJson, string issuer, string audience, string expectedNonce,
        DateTimeOffset now, TimeSpan skew)
    {
        if (string.IsNullOrEmpty(payloadJson) || payloadJson.Length > MaxIdTokenPayloadBytes) { return "payload_size"; }
        try
        {
            using JsonDocument doc = JsonDocument.Parse(payloadJson, new JsonDocumentOptions { MaxDepth = 8 });
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { return "payload_shape"; }

            if (Text(root, "iss") is not { } iss || !string.Equals(iss, issuer, StringComparison.Ordinal)) { return "issuer"; }

            if (!root.TryGetProperty("aud", out JsonElement aud)) { return "audience"; }
            List<string> audiences = aud.ValueKind switch
            {
                JsonValueKind.String => new() { aud.GetString() ?? string.Empty },
                JsonValueKind.Array => aud.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString() ?? string.Empty).ToList(),
                _ => new(),
            };
            if (!audiences.Contains(audience, StringComparer.Ordinal)) { return "audience"; }
            if (audiences.Count > 1 && !string.Equals(Text(root, "azp"), audience, StringComparison.Ordinal)) { return "authorized_party"; }

            if (!root.TryGetProperty("exp", out JsonElement exp) || !exp.TryGetInt64(out long expSeconds)) { return "expiry"; }
            if (DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(expSeconds, 0, 253402300799)) < now - skew) { return "expired"; }
            if (root.TryGetProperty("iat", out JsonElement iat) && iat.TryGetInt64(out long iatSeconds)
                && DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(iatSeconds, 0, 253402300799)) > now + skew) { return "issued_in_future"; }

            if (!SameSecret(Text(root, "nonce"), expectedNonce)) { return "nonce"; }
            if (Text(root, "sub") is not { Length: > 0 and <= 255 }) { return "subject"; }
            return null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            return "payload_shape";
        }
    }

    /// <summary>The claims of a compact JWT, decoded but NOT verified (see the class summary). null when malformed.</summary>
    public static string? DecodeJwtPayload(string jwt)
    {
        if (string.IsNullOrEmpty(jwt) || jwt.Length > 16384) { return null; }
        string[] parts = jwt.Split('.');
        if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0) { return null; }
        try
        {
            string b64 = parts[1].Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            byte[] bytes = Convert.FromBase64String(b64);
            return bytes.Length > MaxIdTokenPayloadBytes ? null : new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException) { return null; }
    }

    private static string? Text(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool IsToken(string? value, int min, int max, bool allowScopeChars = false)
    {
        if (value is null || value.Length < min || value.Length > max) { return false; }
        foreach (char c in value)
        {
            bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c is '-' or '.' or '_' or '~'
                || (allowScopeChars && c is ':' or '/' or '@' or '+');
            if (!ok) { return false; }
        }

        return true;
    }
}

/// <summary>
/// Receives the one redirect an authorization sends back to this computer. Listens on 127.0.0.1
/// only, on a port the system picks, for a few seconds' worth of at most a handful of requests:
/// a favicon fetch or a page that is not the callback is turned away and does not end the sign-in,
/// but neither can anything but the genuine callback complete it. The Host header must be the
/// address we handed out (a name that merely resolves to 127.0.0.1 is how DNS rebinding reaches
/// a local port), the method must be GET, and the request is read through a byte budget.
/// </summary>
internal sealed class LoopbackReceiver : IDisposable
{
    public const int MaxRequestBytes = 8192;
    public const int MaxAttempts = 8;

    private readonly TcpListener _listener;
    private readonly string _path, _state;
    private int _wrongStateAttempts;

    private LoopbackReceiver(TcpListener listener, string path, string state)
    {
        _listener = listener;
        _path = path;
        _state = state;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public int Port { get; }

    public Uri RedirectUri => new($"http://127.0.0.1:{Port}{_path}");

    public static LoopbackReceiver Start(string callbackPath, string expectedState)
    {
        if (callbackPath.Length < 2 || callbackPath[0] != '/' || callbackPath.Any(c => c is '?' or '#' or ' ' || c < ' ' || c > '~'))
            throw new ArgumentException("Invalid callback path.");
        if (string.IsNullOrEmpty(expectedState) || expectedState.Length < 16) throw new ArgumentException("Invalid state.");
        TcpListener listener = new(IPAddress.Loopback, 0) { ExclusiveAddressUse = true };
        listener.Start(4);
        return new LoopbackReceiver(listener, callbackPath, expectedState);
    }

    public async Task<OAuthFlow.CallbackResult> WaitAsync(TimeSpan timeout, CancellationToken cancel = default)
    {
        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        limit.CancelAfter(timeout);
        try
        {
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync(limit.Token).ConfigureAwait(false);
                OAuthFlow.CallbackResult? result = await HandleAsync(client, limit.Token).ConfigureAwait(false);
                if (result is { } done) { return done; }
            }

            return OAuthFlow.CallbackResult.Fail("too_many_requests");
        }
        catch (OperationCanceledException) { return OAuthFlow.CallbackResult.Fail(cancel.IsCancellationRequested ? "cancelled" : "timeout"); }
    }

    /// <returns>The outcome if this request settled the sign-in; null when it was turned away and we keep listening.</returns>
    private async Task<OAuthFlow.CallbackResult?> HandleAsync(TcpClient client, CancellationToken cancel)
    {
        client.NoDelay = true;
        using CancellationTokenSource read = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        read.CancelAfter(TimeSpan.FromSeconds(5));
        NetworkStream stream = client.GetStream();
        string? head;
        try { head = await ReadHeadAsync(stream, read.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested) { return null; }
        catch (IOException) { return null; }
        catch (SocketException) { return null; }

        if (head is null) { await RespondAsync(stream, 431, "Request too large.", null).ConfigureAwait(false); return null; }
        string[] lines = head.Split("\r\n");
        string[] request = lines[0].Split(' ');
        if (request.Length != 3 || request[0] != "GET" || !request[2].StartsWith("HTTP/1.", StringComparison.Ordinal) || !request[1].StartsWith('/'))
        {
            await RespondAsync(stream, 400, "Bad request.", null).ConfigureAwait(false);
            return null;
        }

        string? host = lines.Skip(1).Select(l => l.Split(':', 2)).Where(p => p.Length == 2 && p[0].Trim().Equals("Host", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[1].Trim()).FirstOrDefault();
        if (!string.Equals(host, "127.0.0.1:" + Port, StringComparison.OrdinalIgnoreCase))
        {
            await RespondAsync(stream, 400, "Bad request.", null).ConfigureAwait(false);
            return null;
        }

        int q = request[1].IndexOf('?');
        if (!string.Equals(q < 0 ? request[1] : request[1][..q], _path, StringComparison.Ordinal))
        {
            await RespondAsync(stream, 404, "Not found.", null).ConfigureAwait(false);
            return null;
        }

        OAuthFlow.CallbackResult result = OAuthFlow.ParseCallback(request[1], _path, _state);
        if (!result.Authentic)
        {
            // Not the answer to our request: whoever sent it does not get to end the sign-in. A few of these and we stop.
            await RespondAsync(stream, 400, "This sign-in link is not valid.", null).ConfigureAwait(false);
            return ++_wrongStateAttempts >= 3 ? OAuthFlow.CallbackResult.Fail("state_mismatch") : null;
        }

        await RespondAsync(stream, 200, result.Ok ? "You are signed in. You can close this tab and return to Aviary." : "Sign-in was not completed. You can close this tab and return to Aviary.", "strip").ConfigureAwait(false);
        return result;
    }

    private static async Task<string?> ReadHeadAsync(NetworkStream stream, CancellationToken cancel)
    {
        byte[] buffer = new byte[MaxRequestBytes];
        int length = 0;
        while (length < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(length), cancel).ConfigureAwait(false);
            if (n == 0) { return null; }
            length += n;
            int end = IndexOfHeadEnd(buffer, length);
            if (end >= 0) { return Encoding.ASCII.GetString(buffer, 0, end); }
        }

        return null;
    }

    private static int IndexOfHeadEnd(byte[] b, int length)
    {
        for (int i = 3; i < length; i++)
        {
            if (b[i - 3] == '\r' && b[i - 2] == '\n' && b[i - 1] == '\r' && b[i] == '\n') { return i - 3; }
        }

        return -1;
    }

    private static async Task RespondAsync(NetworkStream stream, int status, string message, string? strip)
    {
        string nonce = OAuthFlow.NewNonce();
        // The message is one of our own fixed sentences; nothing from the request is written back.
        string body = "<!doctype html><meta charset=\"utf-8\"><title>Aviary</title>"
            + "<style>body{font:16px system-ui,sans-serif;margin:3rem;max-width:32rem;color:#222}</style>"
            + "<p>" + message + "</p>"
            + (strip is null ? string.Empty : $"<script nonce=\"{nonce}\">history.replaceState(null,'',location.pathname)</script>");
        string reason = status switch { 200 => "OK", 400 => "Bad Request", 404 => "Not Found", 431 => "Request Header Fields Too Large", _ => "Error" };
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        string head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bodyBytes.Length}\r\n"
            + "Cache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n"
            + $"Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-{nonce}'; base-uri 'none'; form-action 'none'\r\n\r\n";
        try
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head)).ConfigureAwait(false);
            await stream.WriteAsync(bodyBytes).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException) { }
    }

    public void Dispose() => _listener.Stop();
}
