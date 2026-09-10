namespace Aviary;

/// <summary>
/// Reader: strip a page back to its article and re-set it in Aviary's own typography.
///
/// The extraction is a Readability-style scoring pass written from scratch, because no
/// NuGet package may be added. It is deliberately conservative — it would rather decline to
/// offer Reader than offer it and show you a navigation menu. The "is this readable at all"
/// probe runs on every page load so the Aa menu can enable or grey out its Reader row
/// honestly rather than always offering a button that sometimes does nothing.
///
/// It runs entirely inside the page. Nothing is fetched again, nothing is sent anywhere.
/// </summary>
internal static class Reader
{
    /// <summary>
    /// Injected into every document. Exposes window.__aviaryReader with:
    ///   probe()   → {readable, title, byline, words}
    ///   enter(o)  → replaces the document with the reader rendering
    ///   exit()    → restores the original
    ///   restyle(o)→ changes font, size or theme without re-extracting
    /// </summary>
    public const string Script = """
(function(){
  if (window.__aviaryReader) { return; }

  var saved = null;   // the original documentElement.innerHTML, kept for exit()
  var article = null; // the extracted node, kept so restyle need not re-score

  var BLOCK = /(^|\s)(comment|share|sidebar|footer|header|nav|menu|banner|promo|advert|ad-|related|recommend|social|newsletter|subscribe|cookie|popup|modal|breadcrumb|pagination|widget|meta|byline|author-box|tags?)(\s|$|-|_)/i;
  var GOOD  = /(^|\s)(article|content|post|entry|story|body|main|text|prose|markdown)(\s|$|-|_)/i;

  function textOf(n){ return (n.innerText || n.textContent || '').trim(); }

  /* Paragraph density is what separates an article from a link farm. A node full of
     anchors is a navigation block no matter how many words it holds. */
  function linkDensity(n){
    var total = textOf(n).length;
    if (!total) { return 1; }
    var linked = 0;
    var links = n.querySelectorAll('a');
    for (var i = 0; i < links.length; i++){ linked += textOf(links[i]).length; }
    return linked / total;
  }

  function score(n){
    var text = textOf(n);
    if (text.length < 200) { return 0; }

    var s = 0;
    var id = (n.id || '') + ' ' + (n.className || '');
    if (typeof id === 'string'){
      if (GOOD.test(id)) { s += 40; }
      if (BLOCK.test(id)) { s -= 60; }
    }

    var tag = n.tagName;
    if (tag === 'ARTICLE') { s += 60; }
    else if (tag === 'MAIN') { s += 40; }
    else if (tag === 'SECTION') { s += 10; }
    else if (tag === 'ASIDE' || tag === 'NAV' || tag === 'FOOTER' || tag === 'HEADER') { s -= 80; }

    var paras = n.querySelectorAll('p');
    var solid = 0;
    for (var i = 0; i < paras.length; i++){
      if (textOf(paras[i]).length > 80) { solid++; }
    }

    s += Math.min(60, solid * 6);
    s += Math.min(50, text.length / 120);
    s -= linkDensity(n) * 90;

    /* A wrapper that merely contains the article should not beat the article. */
    if (n.children.length === 1 && paras.length === 0) { s -= 25; }

    return s;
  }

  function findArticle(){
    var best = null, bestScore = 0;
    var nodes = document.querySelectorAll('article, main, section, div, td');
    for (var i = 0; i < nodes.length && i < 4000; i++){
      var s = score(nodes[i]);
      if (s > bestScore){ bestScore = s; best = nodes[i]; }
    }
    return bestScore >= 55 ? best : null;
  }

  function findTitle(){
    var h1 = document.querySelector('article h1, main h1, h1');
    if (h1 && textOf(h1).length > 3) { return textOf(h1); }
    var og = document.querySelector('meta[property="og:title"]');
    if (og && og.content) { return og.content; }
    return (document.title || '').split(/\s[|–—-]\s/)[0].trim();
  }

  function findByline(){
    var sel = ['[rel="author"]','.byline','.author','[itemprop="author"]','meta[name="author"]'];
    for (var i = 0; i < sel.length; i++){
      var el = document.querySelector(sel[i]);
      if (!el) { continue; }
      var t = el.tagName === 'META' ? el.content : textOf(el);
      if (t && t.length > 1 && t.length < 120) { return t.replace(/^\s*by\s+/i, ''); }
    }
    return '';
  }

  function clean(node){
    var copy = node.cloneNode(true);
    var junk = copy.querySelectorAll(
      'script,style,noscript,iframe,form,button,input,select,textarea,svg use,' +
      'nav,aside,footer,header,[role="navigation"],[role="banner"],[role="complementary"],' +
      '[aria-hidden="true"],.share,.social,.related,.newsletter,.advert,.ad,ins');
    for (var i = 0; i < junk.length; i++){ junk[i].remove(); }

    /* Strip presentational attributes so the reader's own type is what you see. */
    var all = copy.querySelectorAll('*');
    for (var j = 0; j < all.length; j++){
      var el = all[j];
      el.removeAttribute('style');
      el.removeAttribute('class');
      el.removeAttribute('width');
      el.removeAttribute('height');
      el.removeAttribute('align');
    }

    /* Lazy-loaded images carry the real URL in data-src and a placeholder in src. */
    var imgs = copy.querySelectorAll('img');
    for (var k = 0; k < imgs.length; k++){
      var img = imgs[k];
      var real = img.getAttribute('data-src') || img.getAttribute('data-original');
      if (real) { img.setAttribute('src', real); }
      img.removeAttribute('srcset');
      img.removeAttribute('sizes');
      img.setAttribute('loading', 'lazy');
    }

    return copy;
  }

  var FONTS = {
    'San Francisco': '-apple-system,"Segoe UI Variable Text","Segoe UI",system-ui,sans-serif',
    'New York': 'ui-serif,"Georgia Pro",Georgia,"Times New Roman",serif',
    'Athelas': '"Palatino Linotype",Palatino,Georgia,serif',
    'Charter': '"Bitstream Charter","Charter",Georgia,serif',
    'Georgia': 'Georgia,"Times New Roman",serif',
    'Iowan': '"Iowan Old Style","Palatino Linotype",Georgia,serif',
    'Palatino': '"Palatino Linotype",Palatino,"Book Antiqua",serif',
    'Seravek': '"Seravek","Segoe UI Variable Text","Segoe UI",sans-serif',
    'Times New Roman': '"Times New Roman",Times,serif'
  };

  var THEMES = {
    White:  {bg:'#FFFFFF', ink:'#1A1A1A', soft:'#6B6B6B', rule:'rgba(0,0,0,.12)'},
    Sepia:  {bg:'#F5EFE2', ink:'#3B3226', soft:'#7A6C58', rule:'rgba(59,50,38,.16)'},
    Grey:   {bg:'#4A4A4C', ink:'#EDEDED', soft:'#B4B4B6', rule:'rgba(255,255,255,.18)'},
    Black:  {bg:'#000000', ink:'#D8D8D8', soft:'#8E8E90', rule:'rgba(255,255,255,.14)'}
  };

  function css(o){
    var t = THEMES[o.theme] || THEMES.White;
    var f = FONTS[o.font] || FONTS['New York'];
    var size = Math.max(12, Math.min(28, o.size || 19));

    return 'html,body{margin:0;background:' + t.bg + ';}' +
      '#av-reader{max-width:' + (o.width || 40) + 'em;margin:0 auto;padding:64px 24px 140px;' +
        'font:400 ' + size + 'px/1.62 ' + f + ';color:' + t.ink + ';' +
        '-webkit-font-smoothing:antialiased;}' +
      '#av-reader h1{font-size:1.85em;line-height:1.18;font-weight:700;letter-spacing:-.02em;margin:0 0 .35em;}' +
      '#av-reader .av-meta{color:' + t.soft + ';font-size:.76em;margin:0 0 2em;' +
        'padding-bottom:1.2em;border-bottom:1px solid ' + t.rule + ';}' +
      '#av-reader p{margin:0 0 1.15em;}' +
      '#av-reader h2{font-size:1.3em;margin:1.8em 0 .5em;line-height:1.28;}' +
      '#av-reader h3{font-size:1.12em;margin:1.6em 0 .4em;}' +
      '#av-reader a{color:inherit;text-decoration:underline;text-underline-offset:2px;' +
        'text-decoration-color:' + t.rule + ';}' +
      '#av-reader img{max-width:100%;height:auto;display:block;margin:1.6em auto;border-radius:6px;}' +
      '#av-reader figure{margin:1.8em 0;}' +
      '#av-reader figcaption{color:' + t.soft + ';font-size:.76em;text-align:center;margin-top:.6em;}' +
      '#av-reader blockquote{margin:1.6em 0;padding-left:1.1em;border-left:3px solid ' + t.rule + ';color:' + t.soft + ';}' +
      '#av-reader pre{overflow-x:auto;font-size:.82em;line-height:1.5;padding:1em;' +
        'background:rgba(128,128,128,.12);border-radius:8px;}' +
      '#av-reader code{font-family:Consolas,ui-monospace,monospace;font-size:.86em;}' +
      '#av-reader ul,#av-reader ol{margin:0 0 1.15em;padding-left:1.4em;}' +
      '#av-reader li{margin:.3em 0;}' +
      '#av-reader hr{border:0;border-top:1px solid ' + t.rule + ';margin:2.4em 0;}';
  }

  window.__aviaryReader = {
    probe: function(){
      try {
        var node = findArticle();
        var words = node ? textOf(node).split(/\s+/).length : 0;
        return JSON.stringify({
          readable: !!node && words > 120,
          title: findTitle(),
          byline: findByline(),
          words: words
        });
      } catch(e){ return JSON.stringify({readable:false, title:'', byline:'', words:0}); }
    },

    enter: function(o){
      try {
        o = o || {};
        var node = article || findArticle();
        if (!node) { return false; }
        article = node;

        if (saved === null) { saved = document.documentElement.innerHTML; }

        var title = findTitle(), byline = findByline();
        var body = clean(node);

        document.documentElement.innerHTML =
          '<head><meta charset="utf-8"><title>' + (document.title || '') + '</title>' +
          '<style id="av-reader-css">' + css(o) + '</style></head>' +
          '<body><article id="av-reader">' +
            '<h1></h1><div class="av-meta"></div><div id="av-body"></div>' +
          '</article></body>';

        document.querySelector('#av-reader h1').textContent = title;
        document.querySelector('#av-reader .av-meta').textContent =
          [byline, location.hostname.replace(/^www\./, '')].filter(Boolean).join('  ·  ');
        document.getElementById('av-body').appendChild(body);
        window.scrollTo(0, 0);
        return true;
      } catch(e){ return false; }
    },

    restyle: function(o){
      var el = document.getElementById('av-reader-css');
      if (el) { el.textContent = css(o || {}); }
    },

    exit: function(){
      if (saved === null) { return false; }
      document.documentElement.innerHTML = saved;
      saved = null;
      return true;
    },

    active: function(){ return saved !== null; }
  };
})();
""";
}
