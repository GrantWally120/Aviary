using System;
using System.Globalization;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// How much a tab has loaded, counted only while Settings → Performance is on screen
/// (Alpha Build 5 M4.6).
///
/// <para><b>Why it is not always on.</b> The only honest way to count what a page loads is to look
/// at every response it receives, and WebView2 hands those over with their headers. Doing that all
/// the time would mean Aviary copying every header of every response of every tab for nobody —
/// which is exactly why M4.5 dropped this figure. The Performance page already tells the window
/// when it is being watched (<c>perfWatch on/off</c>), so the handler goes on with the pane and
/// comes off with it, and the count starts from zero each time.</para>
///
/// <para><b>What the number means.</b> It is what the sites said they were sending: the
/// Content-Length they declared. A response without one — anything streamed or chunked — cannot be
/// counted before it has finished arriving, so it is not counted at all, and the page says so
/// rather than pretending the figure is the whole truth. Nothing here is written down, and a
/// private tab counts for itself and is never named.</para>
/// </summary>
internal sealed partial class BrowserTab
{
    private bool _watchingData;
    private long _dataBytes;
    private long _dataResponses;
    private long _dataUnknown;

    /// <summary>Bytes this tab has declared since watching began.</summary>
    public long DataBytes => System.Threading.Interlocked.Read(ref _dataBytes);

    /// <summary>Responses that never said how big they were, so could not be counted.</summary>
    public long DataUnknown => System.Threading.Interlocked.Read(ref _dataUnknown);

    /// <summary>Responses counted since watching began.</summary>
    public long DataResponses => System.Threading.Interlocked.Read(ref _dataResponses);

    /// <summary>Starts or stops counting. Call on the UI thread; safe to call twice.</summary>
    public void WatchData(bool on)
    {
        try
        {
            if (View.CoreWebView2 is not CoreWebView2 core || on == _watchingData)
            {
                return;
            }

            if (on)
            {
                System.Threading.Interlocked.Exchange(ref _dataBytes, 0);
                System.Threading.Interlocked.Exchange(ref _dataResponses, 0);
                System.Threading.Interlocked.Exchange(ref _dataUnknown, 0);
                core.WebResourceResponseReceived += OnDataResponse;
            }
            else
            {
                core.WebResourceResponseReceived -= OnDataResponse;
            }

            _watchingData = on;
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: watch data");
        }
    }

    private void OnDataResponse(CoreWebView2 sender, CoreWebView2WebResourceResponseReceivedEventArgs args)
    {
        try
        {
            System.Threading.Interlocked.Increment(ref _dataResponses);

            // TryGetHeader does not exist on this interface; the collection is the way in, and it
            // throws nothing when the header is missing.
            if (args.Response?.Headers is not { } headers || !headers.Contains("Content-Length"))
            {
                System.Threading.Interlocked.Increment(ref _dataUnknown);
                return;
            }

            if (long.TryParse(headers.GetHeader("Content-Length"), NumberStyles.None, CultureInfo.InvariantCulture, out long length)
                && length > 0)
            {
                System.Threading.Interlocked.Add(ref _dataBytes, length);
            }
            else
            {
                System.Threading.Interlocked.Increment(ref _dataUnknown);
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: count data");
        }
    }
}
