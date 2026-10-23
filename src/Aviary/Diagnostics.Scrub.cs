using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aviary;

/// <summary>
/// The secret filter. Every string that reaches the diagnostics folder comes through
/// <see cref="Scrub"/> first — the writer applies it to every string value of every record,
/// so no call site can forget.
///
/// What it removes, and why each one:
///   - Secret-bearing URL parameters — access_token, code, password, sig and their kin — in
///     the query, the fragment (OAuth's implicit flow puts the token THERE), matrix params,
///     and inside URL-encoded redirect addresses nested in other parameters. The parameter's
///     NAME is kept and its value becomes •••, so the address still says what it was without
///     being a working credential on disk.
///   - user:password@ in an address — and a bare user@ too, because a personal access token
///     passed as the user name is a common way to embed one.
///   - Long random path segments in addresses that look like sign-in, reset, verify or
///     magic-link URLs — "/reset-password/7f3a9c…" is a credential with no "=" anywhere.
///   - JSON Web Tokens (eyJ….….…) and Bearer/Basic credentials anywhere in text.
///   - Card numbers — Luhn-valid runs of 13-19 digits, grouped or not.
///   - data: URLs are collapsed to their type and length; a pasted image is not a log line.
///
/// It deliberately does NOT remove ordinary addresses, titles or search terms. Collecting
/// those was Grant's explicit choice; this filter exists for the one line he drew — secrets.
/// </summary>
internal static partial class Diagnostics
{
    internal const string Mask = "•••";
    private const int MaxString = 4000;

    private static readonly Regex UrlPattern = new(
        @"\b(?:https?|wss?|ftp|file)://[^\s""'<>\\`]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// An address typed WITHOUT its scheme — "example.com/reset/…", "127.0.0.1:8765/…",
    /// "localhost/…" — which is how people type into the address bar. Without this the path
    /// check never saw them: M1's test found a reset token masked in the navigation's target
    /// and whole in the "typed" field beside it. The lookbehind skips the host of an address
    /// that already has its scheme (or sits after an @), which UrlPattern has handled.
    /// </summary>
    private static readonly Regex SchemelessUrlPattern = new(
        @"(?<![/\w@.:\-])(?:(?:[a-z0-9\-]+\.)+[a-z]{2,}|\d{1,3}(?:\.\d{1,3}){3}|localhost)(?::\d{1,5})?/[^\s""'<>\\`]*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex DataUrlPattern = new(
        @"\bdata:(?<type>[a-z0-9.+\-/]*)[^,\s""'<>\\`]*,[^\s""'<>\\`]{48,}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// name=value, led by ?, &amp;, #, ; or the start of the text. The value stops at the next
    /// separator, whitespace, quote or backslash — which is also where a JSON string would end.
    /// </summary>
    /// <remarks>
    /// The value stops at '?' too: ";jsessionid=X?page=2" is two parameters, and letting the
    /// first run on swallowed (and masked) the second.
    /// </remarks>
    private static readonly Regex ParamPattern = new(
        @"(?<lead>^|[?&#;])(?<name>[A-Za-z0-9_.\-\[\]%$]{1,80})=(?<value>[^&#;?\s""'<>\\`]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex JwtPattern = new(
        @"\beyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{6,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BearerPattern = new(
        @"\b(?<scheme>Bearer|Basic|Token)\s+[A-Za-z0-9._~+/\-]{12,}=*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Error messages can contain JSON or complete HTTP headers, not just URLs.
    private static readonly Regex JsonPropertyPattern = new(
        @"""(?<name>(?:\\.|[^""\\]){1,160})""\s*:\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    private static readonly Regex SecretHeaderPattern = new(
        @"(?im)^(?<name>[A-Za-z][A-Za-z0-9\-_]{0,63})[ \t]*:[^\r\n]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    /// <summary>
    /// 13-19 digits, optionally grouped by spaces, dashes, '+' or an encoded space (%20).
    ///
    /// The two lookbehinds are about "%20", whose "2" and "0" are digits: a number may start
    /// straight after one (the second lookbehind allows a preceding digit only if it is that
    /// "0"), but may not start ON one (the first refuses a start right after '%'). Without the
    /// first, "pay%204111%201111…" matched from the "20", failed the checksum, and the real
    /// number behind it went through whole.
    /// </summary>
    private static readonly Regex CardPattern = new(
        @"(?<!%)(?<!(?<!%2)[0-9])(?:[0-9](?:%20|[ \-+])?){12,18}[0-9](?![0-9])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PathTokenPattern = new(
        @"^[A-Za-z0-9._~\-]{16,}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>A parameter whose name CONTAINS one of these is a secret.</summary>
    private static readonly string[] SecretStems =
    {
        "token", "secret", "passw", "passcode", "session", "sessid", "signature",
        "apikey", "api_key", "api-key", "auth", "credential", "jwt", "saml", "cookie",
        "privatekey", "private_key", "hmac", "csrf", "xsrf",
        // Account sign-in: what a login, a second factor or a recovery flow hands back and forth.
        "passphrase", "bearer", "mnemonic", "seedphrase", "securitycode", "recoverycode", "devicecode",
        "clientassertion", "backupcode",
    };

    /// <summary>
    /// A parameter one of whose WORDS is one of these is a secret. Words, not substrings:
    /// "code" must catch code and auth_code but not zipcode; "sig" must catch sig but not
    /// design; "key" must catch api_key and Key-Pair-Id but not keyword or monkey.
    /// </summary>
    private static readonly HashSet<string> SecretWords = new(StringComparer.Ordinal)
    {
        "key", "sig", "code", "pin", "otp", "pwd", "pw", "pass", "ticket", "sid", "verifier",
        "state", "nonce", "assertion",
        "totp", "hotp", "mfa", "seed", "cvv", "cvc",
    };

    /// <summary>A path containing one of these may carry a credential as a path segment.</summary>
    private static readonly string[] AuthPathWords =
    {
        "reset", "password", "passwd", "verify", "verification", "confirm", "activate",
        "activation", "magic", "login", "signin", "sign-in", "auth", "token", "invite",
        "recover", "otp", "2fa", "mfa", "session", "oauth", "sso", "callback", "unsubscribe",
    };

    /// <summary>Every string on its way to disk.</summary>
    internal static string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string s = text;

        if (s.Length >= 64 && s.Contains("data:", StringComparison.OrdinalIgnoreCase))
        {
            s = DataUrlPattern.Replace(s, m =>
                "data:" + m.Groups["type"].Value + " (" + m.Length.ToString("N0") + " characters, not recorded)");
        }

        if (s.Length > MaxString)
        {
            s = s.Substring(0, MaxString) + "…(" + (s.Length - MaxString).ToString("N0") + " more characters)";
        }

        if (s.Contains("://", StringComparison.Ordinal))
        {
            s = UrlPattern.Replace(s, m => MaskAddress(m.Value));
        }

        if (s.IndexOf('/') >= 0)
        {
            s = SchemelessUrlPattern.Replace(s, m => MaskAddress("x://" + m.Value).Substring(4));
        }

        if (s.IndexOf('=') >= 0)
        {
            s = MaskParams(s, 0);
        }

        s = MaskStructuredSecrets(s);

        if (s.Contains("eyJ", StringComparison.Ordinal))
        {
            s = JwtPattern.Replace(s, Mask);
        }

        if (s.Contains("earer ", StringComparison.OrdinalIgnoreCase)
            || s.Contains("asic ", StringComparison.OrdinalIgnoreCase)
            || s.Contains("oken ", StringComparison.OrdinalIgnoreCase))
        {
            s = BearerPattern.Replace(s, m => m.Groups["scheme"].Value + " " + Mask);
        }

        if (CountDigits(s) >= 13)
        {
            s = CardPattern.Replace(s, MaskCard);
        }

        return s;
    }

    /// <summary>user:pass@ and credential-looking path segments. Parameters are MaskParams' job.</summary>
    private static string MaskAddress(string url)
    {
        int scheme = url.IndexOf("://", StringComparison.Ordinal);
        if (scheme <= 0)
        {
            return url;
        }

        int authStart = scheme + 3;
        int authEnd = url.IndexOfAny(new[] { '/', '?', '#' }, authStart);
        if (authEnd < 0)
        {
            authEnd = url.Length;
        }

        int at = url.LastIndexOf('@', authEnd - 1, authEnd - authStart);
        if (at >= authStart)
        {
            url = url.Substring(0, authStart) + Mask + url.Substring(at);
            authEnd = authEnd - (at - authStart) + Mask.Length;
        }

        int pathEnd = url.IndexOfAny(new[] { '?', '#' }, authEnd);
        if (pathEnd < 0)
        {
            pathEnd = url.Length;
        }

        if (pathEnd <= authEnd)
        {
            return url;
        }

        string path = url.Substring(authEnd, pathEnd - authEnd);
        string lower = path.ToLowerInvariant();
        if (!AuthPathWords.Any(w => lower.Contains(w, StringComparison.Ordinal)))
        {
            return url;
        }

        string[] segments = path.Split('/');
        bool changed = false;
        for (int i = 0; i < segments.Length; i++)
        {
            string seg = segments[i];
            if (PathTokenPattern.IsMatch(seg) && seg.Any(char.IsDigit) && seg.Any(char.IsLetter))
            {
                segments[i] = Mask;
                changed = true;
            }
        }

        return changed
            ? url.Substring(0, authEnd) + string.Join('/', segments) + url.Substring(pathEnd)
            : url;
    }

    /// <summary>
    /// Masks secret parameters, and looks inside URL-encoded values for more — a sign-in
    /// link's redirect_uri=https%3A%2F%2F…%3Fcode%3DSECRET carries the code one level down.
    /// </summary>
    private static string MaskParams(string s, int depth) =>
        ParamPattern.Replace(s, m =>
        {
            string name = m.Groups["name"].Value;
            string value = m.Groups["value"].Value;
            string lead = m.Groups["lead"].Value;

            if (value.Length == 0 || value == Mask)
            {
                return m.Value;
            }

            if (IsSecretName(name))
            {
                return lead + name + "=" + Mask;
            }

            // An ENCODED value hides things from every other pattern: q=4111+1111+1111+1111
            // and q=4111%201111%201111%201111 are both a card number that the plain-text
            // check never sees, and a redirect_uri carries its code one level down. So the
            // value is looked at decoded — '+' is a space in a query — and re-encoded only if
            // something in it had to go. Found in M1's own test: a searched card number came
            // through the address bar masked and through the URL whole.
            if (depth < 3 && (value.IndexOf('%') >= 0 || value.IndexOf('+') >= 0))
            {
                // Decoded until it stops changing (three rounds at most): a redirect address
                // inside a redirect address arrives double-encoded, "%253D" for "=", and one
                // round of decoding still hides the parameter underneath.
                string decoded = value.Replace('+', ' ');
                try
                {
                    for (int round = 0; round < 3; round++)
                    {
                        string next = Uri.UnescapeDataString(decoded);
                        if (string.Equals(next, decoded, StringComparison.Ordinal))
                        {
                            break;
                        }

                        decoded = next;
                    }
                }
                catch (Exception)
                {
                    return m.Value;
                }

                if (!string.Equals(decoded, value, StringComparison.Ordinal))
                {
                    string inner = decoded;

                    if (inner.IndexOf('=') >= 0)
                    {
                        inner = MaskParams(inner, depth + 1);
                    }

                    if (inner.Contains("://", StringComparison.Ordinal))
                        inner = UrlPattern.Replace(inner, m => MaskAddress(m.Value));
                    inner = MaskStructuredSecrets(inner);
                    inner = BearerPattern.Replace(inner, m => m.Groups["scheme"].Value + " " + Mask);

                    if (CountDigits(inner) >= 13)
                    {
                        inner = CardPattern.Replace(inner, MaskCard);
                    }

                    if (inner.Contains("eyJ", StringComparison.Ordinal))
                    {
                        inner = JwtPattern.Replace(inner, Mask);
                    }

                    if (!string.Equals(inner, decoded, StringComparison.Ordinal))
                    {
                        return lead + name + "=" + Uri.EscapeDataString(inner);
                    }
                }
            }

            return m.Value;
        });

    private static string MaskStructuredSecrets(string text)
    {
        try
        {
            // Any header line whose NAME is a secret's: Authorization, Cookie, X-Api-Key, but also
            // X-Auth-Token, X-Amz-Security-Token and the ones an account service will add.
            text = SecretHeaderPattern.Replace(text,
                m => IsSecretName(m.Groups["name"].Value) ? m.Groups["name"].Value + ": " + Mask : m.Value);
            StringBuilder? masked = null;
            int copied = 0;
            foreach (Match property in JsonPropertyPattern.Matches(text))
            {
                if (property.Index < copied || !IsSecretName(property.Groups["name"].Value)) { continue; }
                int start = property.Index + property.Length;
                int end = JsonValueEnd(text, start);
                if (end <= start) { continue; }
                masked ??= new StringBuilder(text.Length);
                masked.Append(text, copied, start - copied).Append('"').Append(Mask).Append('"');
                copied = end;
            }
            return masked is null ? text : masked.Append(text, copied, text.Length - copied).ToString();
        }
        catch (RegexMatchTimeoutException)
        {
            // A hostile page error must not force a slow match or reach disk unsanitised.
            return "[diagnostic text omitted: redaction limit]";
        }
    }

    // A diagnostic may contain a JSON fragment rather than a complete document, or be
    // cut off at MaxString. Scan just the recognized secret's value, including arrays
    // and objects, without requiring a valid closing quote/bracket before masking it.
    private static int JsonValueEnd(string text, int start)
    {
        if (start >= text.Length) { return start; }
        bool quoted = text[start] == '"';
        bool container = text[start] is '[' or '{';
        if (!quoted && !container)
        {
            int end = start;
            while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] is not (',' or '}' or ']')) { end++; }
            return end;
        }
        int depth = container ? 1 : 0;
        bool inString = quoted;
        for (int index = start + 1; index < text.Length; index++)
        {
            char c = text[index];
            if (inString)
            {
                if (c == '\\') { index++; }
                else if (c == '"')
                {
                    inString = false;
                    if (quoted) { return index + 1; }
                }
            }
            else if (c == '"') { inString = true; }
            else if (c is '[' or '{') { depth++; }
            else if (c is ']' or '}')
            {
                if (--depth == 0) { return index + 1; }
            }
        }
        return text.Length;
    }

    internal static bool IsSecretName(string raw)
    {
        string name = raw;
        if (name.IndexOf('\\') >= 0)
        {
            try { name = JsonSerializer.Deserialize<string>("\"" + name + "\"") ?? name; }
            catch (JsonException) { /* a non-JSON parameter name is kept as written */ }
        }
        if (name.IndexOf('%') >= 0)
        {
            try
            {
                for (int round = 0; round < 3; round++)
                {
                    string next = Uri.UnescapeDataString(name);
                    if (string.Equals(next, name, StringComparison.Ordinal)) { break; }
                    name = next;
                }
            }
            catch (Exception) { /* keep the raw form */ }
        }

        name = name.Trim('[', ']', '$');
        string lower = name.ToLowerInvariant();

        if (SecretWords.Contains(lower) || SecretStems.Any(stem => lower.Contains(stem, StringComparison.Ordinal)))
        {
            return true;
        }

        foreach (string word in Words(name))
        {
            if (SecretWords.Contains(word))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>"X-Amz-Signature" → x, amz, signature; "apiKey" → api, key; "auth_code2" → auth, code, 2.</summary>
    private static IEnumerable<string> Words(string name)
    {
        StringBuilder word = new();
        char previous = '\0';

        foreach (char c in name)
        {
            bool boundary = !char.IsLetterOrDigit(c)
                || (char.IsUpper(c) && char.IsLower(previous))
                || (char.IsDigit(c) != char.IsDigit(previous) && char.IsLetterOrDigit(previous));

            if (boundary && word.Length > 0)
            {
                yield return word.ToString().ToLowerInvariant();
                word.Clear();
            }

            if (char.IsLetterOrDigit(c))
            {
                word.Append(c);
            }

            previous = c;
        }

        if (word.Length > 0)
        {
            yield return word.ToString().ToLowerInvariant();
        }
    }

    private static int CountDigits(string s)
    {
        int n = 0;
        foreach (char c in s)
        {
            if (c >= '0' && c <= '9')
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>
    /// A card number is 13-19 digits that pass the Luhn check. Written grouped ("4111 1111 …")
    /// it is taken at its word; written as one unbroken run it must ALSO start the way cards
    /// start (2-6), because long numeric IDs — a post's ID in an address, say — are common and
    /// a tenth of them pass Luhn by chance.
    /// </summary>
    private static string MaskCard(Match m)
    {
        // "%20" is a separator here, and its "2" and "0" are not part of the number.
        string value = m.Value.Replace("%20", " ", StringComparison.Ordinal);
        string digits = new(value.Where(c => c >= '0' && c <= '9').ToArray());
        if (digits.Length < 13 || digits.Length > 19 || !Luhn(digits))
        {
            return m.Value;
        }

        bool grouped = value.IndexOf(' ') >= 0 || value.IndexOf('-') >= 0 || value.IndexOf('+') >= 0;
        if (!grouped && (digits[0] < '2' || digits[0] > '6'))
        {
            return m.Value;
        }

        return "[card number removed]";
    }

    private static bool Luhn(string digits)
    {
        int sum = 0;
        bool dbl = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int d = digits[i] - '0';
            if (dbl)
            {
                d *= 2;
                if (d > 9)
                {
                    d -= 9;
                }
            }

            sum += d;
            dbl = !dbl;
        }

        return sum % 10 == 0;
    }
}
