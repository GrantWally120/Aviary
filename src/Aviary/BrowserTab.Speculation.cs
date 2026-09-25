namespace Aviary;

/// <summary>
/// Bug 91 (found 25 Sep 2026, M6 stage 2, measuring "Preload a page when you point at a link"):
/// <b>a website's speculation rules made the engine fetch pages that never passed through Aviary.</b>
///
/// <para>Speculation rules are how a page asks the browser to load other pages ahead of time — on
/// hover, or at once. Google Search uses them, and WordPress has put them on every site since 6.8.
/// The engine carries them out in its own prefetch service, and on this engine those requests are
/// NOT handed to <c>WebResourceRequested</c> — even with every source kind registered (BrowserTab.cs).
/// Measured with a probe that stamped every request the filter was handed and blocked one address:
/// the prefetch of the "blocked" address reached the server, a <c>fetch()</c> of it did not. So a
/// speculative load skipped Shields, the tracking-parameter stripping, client-hint removal, and even
/// GPC (the engine's own GlobalPrivacyControlForce leaves prefetch requests without Sec-GPC). No
/// engine switch turns them off in this version (<c>--disable-blink-features=SpeculationRules</c> and
/// <c>--disable-features=Prerender2,…</c> were both tried; loads still went out).</para>
///
/// <para>So Aviary takes the rules away before the engine acts on them — in the page, the only place
/// it can: <see cref="NoSpeculationScript"/> runs before any of the page's own script, in every frame.
/// <c>HTMLScriptElement.supports('speculationrules')</c> answers false (a site that asks first never
/// adds any), and a rules
/// script that arrives anyway is removed in the same microtask checkpoint, before the engine starts a
/// load; removing a rules script removes its rules. Measured (tools/test/spec-probe.js): rules in the
/// HTML (immediate, on hover, and prerender) and rules added by script all make no request with it,
/// and all do without it. A page loses nothing it needs: speculation is only ever a head start, and
/// every link still opens — through Aviary, like any other load. <c>&lt;link rel=prefetch&gt;</c> is
/// untouched: it DOES go through the filter.</para>
///
/// <para><b>Second look (25 Sep, the stage 2 checks) — four more ways in, each measured loading on the
/// published M5 build and on the first guard, and closed:</b> the engine reads a script as rules when
/// it is added OR when something is put inside it later, with whatever type it has at that moment; and
/// its rules then last as long as the element is in the page, whatever its type says afterwards. So a
/// script added as <c>text/plain</c> (or empty) and turned into rules later, and a rules script whose
/// type is changed AWAY straight after it is added, both got past a guard that only looked at scripts
/// being added. Rules inside a shadow root got past it too (the document's observer does not see into
/// one). Now: type changes are watched with their old value, a script that is (or was) rules is taken
/// out whichever record names it, and every shadow root is watched as it is made (<c>attachShadow</c>
/// is wrapped in a <c>Proxy</c>, which still reads as native code to a page that checks) or as it
/// arrives open in the HTML. Every DOM method and getter the guard calls is taken before the page's
/// own script runs, so a page cannot blind it by redefining them. Measured (spec-probe.js, "late"):
/// all seven of those routes load on the published M5 build and nothing with this guard — including
/// a closed root made with ANOTHER frame's <c>attachShadow</c> (the guard runs in a fresh about:blank
/// frame too, so that frame's copy is wrapped as well).</para>
///
/// <para><b>What is still open, measured and not hidden:</b> rules sent in a <c>Speculation-Rules</c>
/// RESPONSE HEADER, which names a JSON file. That file is fetched through the filter — but at that
/// moment it carries nothing that tells it apart (<c>Accept: */*</c>, context Other, an Origin like any
/// CORS request), and the header naming it can only be read after the fact. Cloudflare's "Speed Brain"
/// uses this route; as far as is known it prefetches same-site pages only when a link is pressed —
/// the page being opened anyway — so what escapes is mostly the GPC signal on that one early request.
/// Also open: a CLOSED shadow root declared in the HTML (nothing outside it can see in). HANDOFF §10,
/// bug 91, keeps the measured list.</para>
/// </summary>
internal sealed partial class BrowserTab
{
    private const string NoSpeculationScript = """
(function () {
  function getter(proto, name) { var d = Object.getOwnPropertyDescriptor(proto, name); return d && d.get; }
  // Everything the guard calls, taken before any of the page's own script runs.
  var removeChild = Node.prototype.removeChild, getAttribute = Element.prototype.getAttribute;
  var inElement = Element.prototype.querySelectorAll, inFragment = DocumentFragment.prototype.querySelectorAll;
  var attachShadow = Element.prototype.attachShadow, observe = MutationObserver.prototype.observe;
  var nodeType = getter(Node.prototype, 'nodeType'), parentOf = getter(Node.prototype, 'parentNode');
  var localName = getter(Element.prototype, 'localName'), firstChildEl = getter(Element.prototype, 'firstElementChild');
  var shadowOf = getter(Element.prototype, 'shadowRoot');
  var recType = getter(MutationRecord.prototype, 'type'), recTarget = getter(MutationRecord.prototype, 'target');
  var recAdded = getter(MutationRecord.prototype, 'addedNodes'), recOld = getter(MutationRecord.prototype, 'oldValue');
  var listLength = getter(NodeList.prototype, 'length'), listItem = NodeList.prototype.item;
  var trim = String.prototype.trim, lower = String.prototype.toLowerCase, apply = Reflect.apply;
  try {
    var supports = HTMLScriptElement.supports;
    if (typeof supports === 'function') {
      Object.defineProperty(HTMLScriptElement, 'supports', {
        value: function (type) {
          return String(type).trim().toLowerCase() === 'speculationrules' ? false : supports.call(HTMLScriptElement, type);
        },
        configurable: true, writable: true
      });
    }
  } catch (e) {}
  function isRulesType(v) { return v != null && lower.call(trim.call(v)) === 'speculationrules'; }
  function isScript(n) { return nodeType.call(n) === 1 && localName.call(n) === 'script'; }
  function isRules(n) { return isScript(n) && isRulesType(getAttribute.call(n, 'type')); }
  function drop(n) { var p = parentOf.call(n); if (p) { removeChild.call(p, n); } }
  function sweep(list) {
    for (var k = listLength.call(list) - 1; k >= 0; k--) { var s = listItem.call(list, k); if (isRules(s)) { drop(s); } }
  }
  var WATCH = { childList: true, subtree: true, attributes: true, attributeFilter: ['type'], attributeOldValue: true };
  var observer = new MutationObserver(function (records) {
    for (var i = 0; i < records.length; i++) {
      var r = records[i], t = recTarget.call(r);
      // A script the engine may already have taken rules from: made rules after it was added and then
      // given text, or changed AWAY straight after it was added. Taking it out takes its rules away.
      if (isScript(t) && (isRules(t) || (recType.call(r) === 'attributes' && isRulesType(recOld.call(r))))) { drop(t); continue; }
      var added = recAdded.call(r);
      for (var j = 0; j < listLength.call(added); j++) {
        var n = listItem.call(added, j);
        if (nodeType.call(n) !== 1) { continue; }
        if (isRules(n)) { drop(n); continue; }
        if (firstChildEl.call(n)) { sweep(apply(inElement, n, ['script'])); }
        var open = shadowOf.call(n);
        if (open) { watch(open); }
      }
    }
  });
  function watch(root) { observe.call(observer, root, WATCH); sweep(apply(inFragment, root, ['script'])); }
  observe.call(observer, document, WATCH);
  // A shadow root is outside the document's tree, so each is watched as it is made - closed ones too.
  if (typeof attachShadow === 'function' && typeof Proxy === 'function') {
    try {
      Element.prototype.attachShadow = new Proxy(attachShadow, {
        apply: function (target, self, args) { var root = apply(target, self, args); try { watch(root); } catch (e) {} return root; }
      });
    } catch (e) {}
  }
})();
""";
}
