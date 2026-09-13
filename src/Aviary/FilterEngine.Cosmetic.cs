using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Aviary;

/// <summary>
/// Element hiding — the <c>##</c> rules that hide the boxes ads leave behind (M3).
///
/// <para><b>Measured before it was designed</b> (13 Sep, WebView2 152, in a test instance): the
/// 13,635 generic selectors, as ONE constructed stylesheet, take ~1.5–3 ms to parse per page, and
/// on a 12,000-element page a full style recalculation took 19–28 ms with or without them —
/// within noise. 96% of them are a single <c>.class</c> or <c>#id</c>, which Chromium files by
/// that class or id and never tries against anything else. So the plan's "one stylesheet per
/// document" is also the fast way.</para>
///
/// <para><b>How it reaches the page.</b> Everything — the generic selectors, the 7,778 sites' own
/// selectors, the exceptions, the sites whose Shields are down — is compiled into one script
/// that WebView2 runs at the start of EVERY document, frames included (nested ones too, which a
/// host-side injection could not reach on this SDK). The script works out which rules apply to
/// its own document and adopts one stylesheet. The site data is one string searched with
/// indexOf, never parsed as a whole. Nothing in it reaches the host, and it posts nothing.</para>
///
/// <para><b>Safety.</b> Lists are updated from the network, so a selector is data that ends up in
/// CSS: anything that could close the rule and start another (<c>{ }</c>, a comment, an at-rule),
/// or that is unbalanced, is refused at compile time. The worst a hostile selector could then do
/// is hide something.</para>
/// </summary>
internal sealed partial class FilterEngine
{
    /// <summary>The element-hiding rules, compiled for the page script.</summary>
    public CosmeticData Cosmetic { get; private set; } = CosmeticData.Empty;

    public sealed class CosmeticData
    {
        public static readonly CosmeticData Empty = new();

        /// <summary>Generic selectors, one per line.</summary>
        public string Generic = string.Empty;

        /// <summary>"\n" + domain + "\t" + selectors joined by U+0002, per domain, "\n"-terminated.</summary>
        public string Specific = "\n";

        /// <summary>The same shape: selectors NOT to hide on that domain (<c>#@#</c>).</summary>
        public string Exceptions = "\n";

        /// <summary>
        /// Page-level exceptions as JavaScript regular expressions:
        /// [kind ("doc" | "eh" | "gh"), source, flags, include domains, exclude domains].
        /// </summary>
        public List<object[]> PageRules = new();
    }

    private sealed class CosmeticBuilder
    {
        private readonly List<string> _generic = new();
        private readonly HashSet<string> _genericSeen = new(StringComparer.Ordinal);
        private readonly HashSet<string> _genericExcepted = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _specific = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _exceptions = new(StringComparer.Ordinal);

        /// <summary>
        /// "domains##selector", "domains#@#selector", and the extended forms this engine does not
        /// run (#?#, #$#, #%#, #@?#…). The domains part cannot hold / * | @ " ! — which is what
        /// keeps a network rule with a '#' in its URL from being mistaken for one.
        /// </summary>
        public static bool TrySplit(string line, out string domains, out string separator, out string body)
        {
            domains = separator = body = string.Empty;
            int hash = line.IndexOf('#');
            while (hash >= 0 && hash < line.Length - 1)
            {
                int end = -1;
                if (line[hash + 1] == '#') { end = hash + 2; }
                else if (hash + 2 < line.Length && line[hash + 2] == '#' && line[hash + 1] is '@' or '?' or '$' or '%') { end = hash + 3; }
                else if (hash + 3 < line.Length && line[hash + 1] == '@' && line[hash + 2] is '?' or '$' or '%' && line[hash + 3] == '#') { end = hash + 4; }

                if (end > 0)
                {
                    string d = line.Substring(0, hash);
                    if (d.IndexOfAny(NotInDomains) >= 0)
                    {
                        return false;
                    }

                    domains = d;
                    separator = line.Substring(hash, end - hash);
                    body = line.Substring(end);
                    return body.Length > 0;
                }

                hash = line.IndexOf('#', hash + 1);
            }

            return false;
        }

        private static readonly char[] NotInDomains = { '/', '|', '@', '"', '!', '$' };

        public void Add(string line, string domains, string separator, string body, ListStats stats)
        {
            if (separator != "##" && separator != "#@#")
            {
                stats.Skip("extended element hiding (" + separator + ")", line);
                return;
            }

            if (body.StartsWith("+js(", StringComparison.Ordinal) || body[0] == '^')
            {
                stats.Skip("scriptlet or HTML filter", line);
                return;
            }

            if (IsProcedural(body))
            {
                stats.Skip("procedural selector", line);
                return;
            }

            if (!SafeSelector(body))
            {
                stats.Skip("unsafe or malformed selector", line);
                return;
            }

            List<string> include = new(), exclude = new();
            foreach (string raw in domains.Split(','))
            {
                string d = raw.Trim().ToLowerInvariant();
                if (d.Length == 0 || d == "*")
                {
                    continue;
                }

                bool negated = d[0] == '~';
                if (negated) { d = d.Substring(1); }
                if (!ValidDomain(d))
                {
                    stats.Skip("unreadable domain", line);
                    return;
                }

                (negated ? exclude : include).Add(d);
            }

            if (separator == "#@#")
            {
                stats.CosmeticExceptions++;
                if (include.Count == 0)
                {
                    // Excepted everywhere: simply never hidden generically.
                    _genericExcepted.Add(body);
                    return;
                }

                foreach (string d in include)
                {
                    AddTo(_exceptions, d, body);
                }

                return;
            }

            if (include.Count == 0)
            {
                stats.CosmeticGeneric++;
                if (_genericSeen.Add(body))
                {
                    _generic.Add(body);
                }
            }
            else
            {
                stats.CosmeticSpecific++;
                foreach (string d in include)
                {
                    AddTo(_specific, d, body);
                }
            }

            // "example.com,~shop.example.com##.ad": not on shop.example.com — which is what an
            // exception for that host says.
            foreach (string d in exclude)
            {
                AddTo(_exceptions, d, body);
            }
        }

        private static void AddTo(Dictionary<string, List<string>> map, string domain, string selector)
        {
            if (!map.TryGetValue(domain, out List<string>? list))
            {
                map[domain] = list = new List<string>(2);
            }

            if (!list.Contains(selector))
            {
                list.Add(selector);
            }
        }

        public CosmeticData Build(ListStats stats, List<NetRule> pageExceptions)
        {
            CosmeticData data = new()
            {
                Generic = string.Join('\n', _generic.Where(s => !_genericExcepted.Contains(s))),
                Specific = Pack(_specific),
                Exceptions = Pack(_exceptions),
            };

            foreach (NetRule rule in pageExceptions)
            {
                string kind = rule.Is(RuleFlags.DocumentExc) ? "doc" : rule.Is(RuleFlags.ElemHide) ? "eh" : "gh";
                data.PageRules.Add(new object[]
                {
                    kind,
                    RegexSource(rule),
                    rule.Is(RuleFlags.MatchCase) ? "" : "i",
                    rule.Include ?? Array.Empty<string>(),
                    rule.Exclude ?? Array.Empty<string>(),
                });
            }

            return data;
        }

        private static string Pack(Dictionary<string, List<string>> map)
        {
            StringBuilder sb = new("\n");
            foreach ((string domain, List<string> selectors) in map)
            {
                sb.Append(domain).Append('\t').AppendJoin('', selectors).Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>A page-level exception's pattern as a JavaScript regular expression over location.href.</summary>
        private static string RegexSource(NetRule rule)
        {
            StringBuilder sb = new();
            if (rule.Is(RuleFlags.HostAnchor))
            {
                // Any scheme, then optionally some labels, then the pattern at a label's start.
                sb.Append(@"^[a-z][a-z0-9+.\-]*:\/\/(?:[^\/?#]*\.)?");
            }
            else if (rule.Is(RuleFlags.StartAnchor))
            {
                sb.Append('^');
            }

            foreach (char c in rule.Pattern)
            {
                switch (c)
                {
                    case '*': sb.Append(".*"); break;
                    case '^': sb.Append(@"(?:[^a-zA-Z0-9_.%\-]|$)"); break;
                    default:
                        if (@"\^$.|?*+()[]{}/-".Contains(c)) { sb.Append('\\'); }
                        sb.Append(c);
                        break;
                }
            }

            if (rule.Is(RuleFlags.EndAnchor))
            {
                sb.Append('$');
            }

            return sb.ToString();
        }

        /// <summary>uBO's procedural and action selectors: not CSS, so not something a stylesheet can hold.</summary>
        private static bool IsProcedural(string selector)
        {
            foreach (string p in Procedural)
            {
                if (selector.Contains(p, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static readonly string[] Procedural =
        {
            ":-abp-", ":has-text(", ":contains(", ":xpath(", ":upward(", ":remove(", ":style(",
            ":matches-css", ":matches-attr(", ":matches-path(", ":matches-prop(", ":min-text-length(",
            ":watch-attr(", ":others(", ":remove-attr(", ":remove-class(", ":nth-ancestor(",
        };

        /// <summary>
        /// Refuses anything that could end the hiding rule and start another — braces, comments,
        /// an at-rule, HTML comment tokens — and anything unbalanced, which would swallow the
        /// selectors after it. Brackets, parentheses and quotes are counted outside strings.
        /// </summary>
        internal static bool SafeSelector(string s)
        {
            if (s.Length == 0 || s.Length > 1000 || s[0] == '@')
            {
                return false;
            }

            if (s.Contains("/*", StringComparison.Ordinal) || s.Contains("*/", StringComparison.Ordinal)
                || s.Contains("<!--", StringComparison.Ordinal) || s.Contains("-->", StringComparison.Ordinal))
            {
                return false;
            }

            int square = 0, round = 0;
            char quote = '\0';
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c is '{' or '}' or ';' && quote == '\0')
                {
                    return false;
                }

                if (c is '\n' or '\r' or '\t' or '' or '\0')
                {
                    return false;
                }

                if (c == '\\')
                {
                    // An escape: the next character is literal. A trailing backslash is not.
                    if (i + 1 >= s.Length) { return false; }
                    if (s[i + 1] is '{' or '}') { return false; }
                    i++;
                    continue;
                }

                if (quote != '\0')
                {
                    if (c == quote) { quote = '\0'; }
                    else if (c is '{' or '}') { return false; }
                    continue;
                }

                switch (c)
                {
                    case '"' or '\'': quote = c; break;
                    case '[': square++; break;
                    case ']': if (--square < 0) { return false; } break;
                    case '(': round++; break;
                    case ')': if (--round < 0) { return false; } break;
                }
            }

            return quote == '\0' && square == 0 && round == 0;
        }

        private static bool ValidDomain(string d)
        {
            if (d.Length == 0 || d.Length > 253)
            {
                return false;
            }

            foreach (char c in d)
            {
                if (!(c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-' or '_' or '*' || c > 127))
                {
                    return false;
                }
            }

            return !d.Contains('*') || d.EndsWith(".*", StringComparison.Ordinal);
        }
    }

    // ══ THE PAGE SCRIPT ═════════════════════════════════════════════════════

    private static readonly JsonSerializerOptions ScriptJson = new()
    {
        // Into a JavaScript string, never HTML: '<' and '>' (a child combinator in hundreds of
        // selectors) need no >. Quotes, backslashes and control characters still escape.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The script run at the start of every document. <paramref name="shieldsDown"/> are the
    /// sites (registrable domains) whose pages get no hiding; <paramref name="skipHosts"/> are
    /// hosts never touched at all (Aviary's own).
    /// </summary>
    public string CosmeticScript(IEnumerable<string> shieldsDown, IEnumerable<string> skipHosts)
    {
        var data = new
        {
            off = shieldsDown.ToArray(),
            skip = skipHosts.ToArray(),
            psl = PublicSuffix.MultiLabelSuffixes.Select(s => s.ToLowerInvariant()).ToArray(),
            g = Cosmetic.Generic,
            s = Cosmetic.Specific,
            x = Cosmetic.Exceptions,
            p = Cosmetic.PageRules,
        };

        return CosmeticTemplate.Replace("__AVIARY_COSMETIC__", JsonSerializer.Serialize(data, ScriptJson));
    }

    /// <summary>
    /// Runs before any of the page's own scripts, in every frame. Decides what to hide for THIS
    /// document from its own address and its top page's (location.ancestorOrigins — a frame
    /// cannot otherwise see the page it is on), then adopts one stylesheet. A setter on this
    /// document keeps the sheet adopted if the page later replaces the whole list.
    /// </summary>
    private const string CosmeticTemplate = """
(function () {
  'use strict';
  var C = __AVIARY_COSMETIC__;
  function under(h, list) {
    for (var i = 0; i < list.length; i++) {
      var d = list[i];
      if (h === d || (h.length > d.length && h.charCodeAt(h.length - d.length - 1) === 46 && h.slice(-d.length) === d)) { return true; }
    }
    return false;
  }
  function local(h) {
    return h.indexOf('.') < 0 || h.charAt(0) === '[' ||
      /^(127\.|10\.|192\.168\.|169\.254\.|172\.(1[6-9]|2\d|3[01])\.)|\.local$|\.localhost$|\.internal$/.test(h);
  }
  /* example.co.uk's keys: example.co.uk, co.uk, then example.* — every name a rule could be under. */
  function keys(h) {
    var parts = h.split('.'), out = [], n = parts.length, i;
    for (i = 0; i < n - 1; i++) { out.push(parts.slice(i).join('.')); }
    var sfx = (n >= 3 && C.psl.indexOf(parts[n - 2] + '.' + parts[n - 1]) >= 0) ? 2 : 1;
    for (i = 0; i < n - sfx; i++) { out.push(parts.slice(i, n - sfx).join('.') + '.*'); }
    return out;
  }
  function look(data, key, out) {
    var needle = '\n' + key + '\t', at = data.indexOf(needle);
    if (at < 0) { return; }
    var from = at + needle.length, to = data.indexOf('\n', from);
    var got = data.slice(from, to < 0 ? data.length : to).split('');
    for (var i = 0; i < got.length; i++) { out.push(got[i]); }
  }
  function inAny(list, ks) {
    for (var i = 0; i < list.length; i++) { if (ks.indexOf(list[i]) >= 0) { return true; } }
    return false;
  }
  function adopt(sheet) {
    var d = document;
    d.adoptedStyleSheets = Array.prototype.slice.call(d.adoptedStyleSheets).concat([sheet]);
    try {
      var desc = Object.getOwnPropertyDescriptor(Document.prototype, 'adoptedStyleSheets');
      if (desc && desc.get && desc.set) {
        Object.defineProperty(d, 'adoptedStyleSheets', {
          configurable: true, enumerable: desc.enumerable,
          get: function () { return desc.get.call(this); },
          set: function (v) {
            var a = Array.prototype.slice.call(v);
            if (a.indexOf(sheet) < 0) { a.push(sheet); }
            desc.set.call(this, a);
          }
        });
      }
    } catch (e) {}
  }
  try {
    var L = location;
    if (L.protocol !== 'http:' && L.protocol !== 'https:') { return; }
    var host = (L.hostname || '').toLowerCase();
    if (!host) { return; }
    var top = host, ao = L.ancestorOrigins;
    if (ao && ao.length) {
      try { top = (new URL(ao[ao.length - 1]).hostname || host).toLowerCase(); } catch (e) {}
    }
    if (under(top, C.off) || under(host, C.skip) || under(top, C.skip) || local(host)) { return; }
    /* A frame Aviary blocked is an empty 403 of its own: nothing in it to hide. */
    if (window !== window.top) {
      try {
        var nav = performance.getEntriesByType('navigation')[0];
        if (nav && nav.responseStatus === 403) { return; }
      } catch (e) {}
    }
    var ks = keys(host), all = [host].concat(ks), generic = true, i;
    for (i = 0; i < C.p.length; i++) {
      var r = C.p[i];
      if (r[3].length && !inAny(r[3], all)) { continue; }
      if (r[4].length && inAny(r[4], all)) { continue; }
      if (new RegExp(r[1], r[2]).test(L.href)) {
        if (r[0] !== 'gh') { return; }
        generic = false;
      }
    }
    var mine = [], except = [];
    for (i = 0; i < ks.length; i++) { look(C.s, ks[i], mine); look(C.x, ks[i], except); }
    var list = (generic && C.g) ? C.g.split('\n').concat(mine) : mine;
    if (except.length) {
      var ex = new Set(except);
      list = list.filter(function (s) { return !ex.has(s); });
    }
    if (!list.length) { return; }
    var sheet = new CSSStyleSheet();
    for (i = 0; i < list.length; i += 200) {
      var chunk = list.slice(i, i + 200);
      try {
        sheet.insertRule(chunk.join(',') + '{display:none!important}', sheet.cssRules.length);
      } catch (e) {
        /* One selector this engine cannot read must not cost the other 199. */
        for (var k = 0; k < chunk.length; k++) {
          try { sheet.insertRule(chunk[k] + '{display:none!important}', sheet.cssRules.length); } catch (e2) {}
        }
      }
    }
    adopt(sheet);
  } catch (e) {}
})();
""";
}
