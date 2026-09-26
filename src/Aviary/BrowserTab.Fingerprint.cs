using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// Settings → Experimental → "Stronger fingerprint protection" (Alpha Build 5 M6 stage 4) — the M3
/// Shields row that was only ever promised.
///
/// <para><b>Measured first</b> (tools/test/expsite.js /fp): with nothing on, a script reading a canvas
/// drawing, an audio rendering, the graphics card's names, the core count and the memory got the SAME
/// answers on two different websites and after a restart — the exact card ("NVIDIA GeForce RTX 5070 Ti"),
/// 16 cores, 32 GB. Enough to recognise this Aviary anywhere, cookies or not.</para>
///
/// <para><b>What it does</b> — Brave's "farbling", in a page script that runs before any of the page's
/// own, in every frame: what a script READS BACK from a canvas (getImageData, toDataURL, toBlob, WebGL's
/// readPixels, an OffscreenCanvas's) carries a tiny noise, and so does what it reads from an audio
/// rendering. The noise is seeded by a per-launch key and the top page's site — so every website sees a
/// different value, a restart gives each a new one, and within one visit the same drawing always reads
/// the same (sites that compare two reads keep working). The core count and memory answer 8 and 8, and
/// the graphics card a common one. Each replaced function is a <c>Proxy</c> of the real one, so it still
/// reads as native code to a page that checks.</para>
///
/// <para><b>Not covered, and said on the row:</b> web workers — a page script cannot reach into them —
/// so a script that does the same readings inside a worker still sees the real values. Fonts are not
/// touched either. It applies to pages opened after it is switched on.</para>
/// </summary>
internal sealed partial class BrowserTab
{
    /// <summary>A new one every time Aviary starts: yesterday's answers cannot be matched with today's.</summary>
    private static readonly string FingerprintSession = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    private string? _fingerprintScriptId;

    /// <summary>Registers the defences for every document this tab creates from now on, or takes them away.</summary>
    public async Task RefreshFingerprintAsync()
    {
        try
        {
            if (View.CoreWebView2 is not CoreWebView2 core)
            {
                return;
            }

            bool want = AppSettings.GetBool("exp.fingerprint");
            if (want && _fingerprintScriptId is null)
            {
                string id = await core.AddScriptToExecuteOnDocumentCreatedAsync(
                    FingerprintScript.Replace("__AVIARY_FP_SESSION__", FingerprintSession));

                // Switched off while it was being added, or added twice at once: keep one or none.
                if (!AppSettings.GetBool("exp.fingerprint") || _fingerprintScriptId is not null)
                {
                    core.RemoveScriptToExecuteOnDocumentCreated(id);
                    return;
                }

                _fingerprintScriptId = id;
            }
            else if (!want && _fingerprintScriptId is not null)
            {
                core.RemoveScriptToExecuteOnDocumentCreated(_fingerprintScriptId);
                _fingerprintScriptId = null;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: fingerprint defences");
        }
    }

    private const string FingerprintScript = """
(function () {
  'use strict';
  var SESSION = '__AVIARY_FP_SESSION__';
  // The site is the TOP page's: a frame asks its ancestors, so one website sees one value everywhere in it.
  var site = location.hostname;
  try { var ao = location.ancestorOrigins; if (ao && ao.length) { site = new URL(ao[ao.length - 1]).hostname; } } catch (e) {}
  function fnv(s) { var x = 2166136261 >>> 0; for (var i = 0; i < s.length; i++) { x ^= s.charCodeAt(i); x = Math.imul(x, 16777619) >>> 0; } return x >>> 0; }
  var seed = fnv(SESSION + '|' + site);
  function rnd(n) { var x = (seed ^ Math.imul(n + 1, 2654435761)) >>> 0; x ^= x << 13; x >>>= 0; x ^= x >>> 17; x ^= x << 5; return x >>> 0; }
  var apply = Reflect.apply, P = Proxy;
  function wrap(owner, name, make) {
    var real = owner && owner[name];
    if (typeof real !== 'function') { return; }
    try { owner[name] = new P(real, { apply: make(real) }); } catch (e) {}
  }
  // Canvas: about one pixel in sixteen gets its lowest bits flipped - by position, so a read is repeatable.
  // Only opaque ones: a real canvas reads a see-through pixel as colourless, so colour in one would be
  // noise a page could spot - and there a flip can only add, so it skewed the drawing (measured 26 Sep).
  function farble(d) {
    for (var i = 0; i < d.length; i += 4) {
      if (d[i + 3] !== 255) { continue; }
      var r = rnd(i >> 2);
      if ((r & 15) === 0) { d[i] ^= (r >>> 4) & 1; d[i + 1] ^= (r >>> 5) & 1; d[i + 2] ^= (r >>> 6) & 1; }
    }
  }
  var C2D = window.CanvasRenderingContext2D && CanvasRenderingContext2D.prototype;
  var getImageData = C2D && C2D.getImageData, putImageData = C2D && C2D.putImageData;
  wrap(C2D, 'getImageData', function (real) { return function (t, self, args) { var img = apply(real, self, args); farble(img.data); return img; }; });
  var O2D = window.OffscreenCanvasRenderingContext2D && OffscreenCanvasRenderingContext2D.prototype;
  wrap(O2D, 'getImageData', function (real) { return function (t, self, args) { var img = apply(real, self, args); farble(img.data); return img; }; });
  // toDataURL / toBlob read the WHOLE canvas: they read a noisy copy of it. A canvas that may not be read
  // (a picture from another site drawn on it) fails the copy, and the real call then fails as it should.
  function noisyCopy(canvas, offscreen) {
    var w = canvas.width, h = canvas.height;
    if (!w || !h) { return null; }
    var c = offscreen ? new OffscreenCanvas(w, h) : document.createElement('canvas');
    if (!offscreen) { c.width = w; c.height = h; }
    var g = c.getContext('2d');
    g.drawImage(canvas, 0, 0);
    var img = apply(getImageData, g, [0, 0, w, h]); farble(img.data); apply(putImageData, g, [img, 0, 0]);
    return c;
  }
  function viaCopy(offscreen) { return function (real) { return function (t, self, args) { var c = null; try { c = noisyCopy(self, offscreen); } catch (e) {} return apply(real, c || self, args); }; }; }
  var HC = window.HTMLCanvasElement && HTMLCanvasElement.prototype;
  wrap(HC, 'toDataURL', viaCopy(false));
  wrap(HC, 'toBlob', viaCopy(false));
  wrap(window.OffscreenCanvas && OffscreenCanvas.prototype, 'convertToBlob', viaCopy(true));
  [window.WebGLRenderingContext, window.WebGL2RenderingContext].forEach(function (GL) {
    if (!GL) { return; }
    wrap(GL.prototype, 'readPixels', function (real) { return function (t, self, args) { var r = apply(real, self, args); var px = args[6]; if (px && px.length && px.BYTES_PER_ELEMENT === 1) { farble(px); } return r; }; });
    // The graphics card: a common one, not this machine's.
    wrap(GL.prototype, 'getParameter', function (real) { return function (t, self, args) {
      if (args[0] === 0x9245) { return 'Google Inc. (Intel)'; }
      if (args[0] === 0x9246) { return 'ANGLE (Intel, Intel(R) UHD Graphics 620 Direct3D11 vs_5_0 ps_5_0, D3D11)'; }
      return apply(real, self, args); }; });
  });
  // Audio: every sample a script reads is scaled by one factor a hair from 1, once per buffer.
  var k = 1 + (((seed % 997) + 1) * ((seed & 1) ? 1 : -1)) * 5e-8;
  var done = new WeakSet();
  function scale(a) { if (!a || done.has(a)) { return; } done.add(a); for (var i = 0; i < a.length; i++) { a[i] *= k; } }
  var AB = window.AudioBuffer && AudioBuffer.prototype;
  wrap(AB, 'getChannelData', function (real) { return function (t, self, args) { var d = apply(real, self, args); try { scale(d); } catch (e) {} return d; }; });
  wrap(AB, 'copyFromChannel', function (real) { return function (t, self, args) { var r = apply(real, self, args); try { var a = args[0]; for (var i = 0; i < a.length; i++) { a[i] *= k; } } catch (e) {} return r; }; });
  var AN = window.AnalyserNode && AnalyserNode.prototype;
  ['getFloatFrequencyData', 'getFloatTimeDomainData'].forEach(function (n) {
    wrap(AN, n, function (real) { return function (t, self, args) { var r = apply(real, self, args); try { var a = args[0]; for (var i = 0; i < a.length; i++) { a[i] *= k; } } catch (e) {} return r; }; });
  });
  // Cores and memory: the answers most computers give.
  function answer(owner, name, value) {
    var d = owner && Object.getOwnPropertyDescriptor(owner, name);
    if (!d || typeof d.get !== 'function') { return; }
    try { Object.defineProperty(owner, name, { get: new P(d.get, { apply: function () { return value; } }), set: d.set, enumerable: d.enumerable, configurable: true }); } catch (e) {}
  }
  var NV = window.Navigator && Navigator.prototype;
  answer(NV, 'hardwareConcurrency', 8);
  answer(NV, 'deviceMemory', 8);
})();
""";
}
