using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// Find in page, done with the CSS Custom Highlight API rather than by wrapping matches in
/// elements.
///
/// The usual approach — walk the text nodes and wrap each hit in a &lt;mark&gt; — mutates
/// the page's DOM. On a modern site that means firing mutation observers, invalidating
/// React's virtual DOM and occasionally breaking the page outright, and then you have to
/// unwrap it all again perfectly. CSS.highlights paints ranges without touching the
/// document at all, so a page cannot tell it is being searched.
/// </summary>
internal static class FindInPage
{
    /// <summary>
    /// Injected into every document. Two highlight groups: every match in a soft amber,
    /// the current one in a stronger orange, which is how Safari distinguishes them.
    /// </summary>
    public const string Script = """
(function(){
  if (window.__aviaryFind) { return; }

  var ranges = [], idx = -1, styled = false;

  function ensureStyle(){
    if (styled) { return; }
    styled = true;
    try {
      var s = document.createElement('style');
      s.textContent =
        '::highlight(aviary-find){background-color:#FFE9A8;color:#000;}' +
        '::highlight(aviary-find-current){background-color:#FF9F0A;color:#000;}';
      (document.head || document.documentElement).appendChild(s);
    } catch(e){}
  }

  function clear(){
    try { CSS.highlights.delete('aviary-find'); CSS.highlights.delete('aviary-find-current'); } catch(e){}
    ranges = []; idx = -1;
  }

  function paint(){
    try {
      var all = new Highlight(), cur = new Highlight();
      for (var i = 0; i < ranges.length; i++){ (i === idx ? cur : all).add(ranges[i]); }
      CSS.highlights.set('aviary-find', all);
      CSS.highlights.set('aviary-find-current', cur);
    } catch(e){}
  }

  function reveal(){
    if (idx < 0 || !ranges[idx]) { return; }
    try {
      var r = ranges[idx].getBoundingClientRect();
      if (r.top < 80 || r.bottom > window.innerHeight - 80){
        window.scrollBy(0, r.top - (window.innerHeight / 2));
      }
    } catch(e){}
  }

  window.__aviaryFind = {
    start: function(term){
      ensureStyle();
      clear();
      if (!term) { return 0; }

      var needle = String(term).toLowerCase();
      var walker = document.createTreeWalker(document.body || document.documentElement,
        NodeFilter.SHOW_TEXT, {
          acceptNode: function(n){
            if (!n.nodeValue || !n.nodeValue.trim()) { return NodeFilter.FILTER_REJECT; }
            var p = n.parentElement;
            if (!p) { return NodeFilter.FILTER_REJECT; }
            var t = p.tagName;
            if (t === 'SCRIPT' || t === 'STYLE' || t === 'NOSCRIPT' || t === 'TEXTAREA') {
              return NodeFilter.FILTER_REJECT;
            }
            return NodeFilter.FILTER_ACCEPT;
          }
        });

      var node, guard = 0;
      /* Capped: a search for "e" on a long page would otherwise build tens of thousands
         of ranges and stall the tab. */
      while ((node = walker.nextNode()) && ranges.length < 2000 && guard++ < 300000){
        var hay = node.nodeValue.toLowerCase(), from = 0, at;
        while ((at = hay.indexOf(needle, from)) !== -1){
          try {
            var rg = document.createRange();
            rg.setStart(node, at);
            rg.setEnd(node, at + needle.length);
            ranges.push(rg);
          } catch(e){}
          from = at + needle.length;
          if (ranges.length >= 2000) { break; }
        }
      }

      idx = ranges.length ? 0 : -1;
      paint();
      reveal();
      return ranges.length;
    },

    step: function(dir){
      if (!ranges.length) { return 0; }
      idx = (idx + dir + ranges.length) % ranges.length;
      paint();
      reveal();
      return idx + 1;
    },

    current: function(){ return idx + 1; },

    stop: function(){ clear(); }
  };
})();
""";

    private static async Task<string> RunAsync(BrowserTab? tab, string expression)
    {
        try
        {
            if (tab?.View.CoreWebView2 is not CoreWebView2 core)
            {
                return "0";
            }

            return await core.ExecuteScriptAsync(expression);
        }
        catch (Exception)
        {
            return "0";
        }
    }

    private static int AsInt(string json) =>
        int.TryParse(json?.Trim('"'), out int value) ? value : 0;

    /// <summary>Starts a search and returns the number of matches.</summary>
    public static async Task<int> StartAsync(BrowserTab? tab, string term)
    {
        string encoded = JsonSerializer.Serialize(term);
        return AsInt(await RunAsync(tab, "window.__aviaryFind ? window.__aviaryFind.start(" + encoded + ") : 0"));
    }

    /// <summary>Moves to the next (+1) or previous (-1) match; returns its 1-based index.</summary>
    public static async Task<int> StepAsync(BrowserTab? tab, int direction)
    {
        return AsInt(await RunAsync(tab, "window.__aviaryFind ? window.__aviaryFind.step(" + direction + ") : 0"));
    }

    public static async Task StopAsync(BrowserTab? tab)
    {
        await RunAsync(tab, "window.__aviaryFind && window.__aviaryFind.stop()");
    }
}
