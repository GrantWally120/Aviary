using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Aviary;

/// <summary>The supplementary icon fetcher follows the page's Shields rules at every redirect.</summary>
internal static class IconDownload
{
    internal const int MaxBytes = 256 * 1024;
    private const int MaxRedirects = 3;
    private static readonly HttpClient Http = CreateClient();

    internal sealed record Result(string Url, int Status, string? MediaType, byte[] Bytes);

    private static HttpClient CreateClient()
    {
        HttpClient http = new(new SocketsHttpHandler
        {
            UseCookies = false,
            Credentials = null,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
        }) { Timeout = TimeSpan.FromSeconds(8) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 "
            + "(KHTML, like Gecko) Version/18.0 Safari/605.1.15");
        return http;
    }

    internal static bool Allowed(Uri page, Uri target)
    {
        if (page.Scheme is not ("http" or "https") || target.Scheme is not ("http" or "https")
            || target.UserInfo.Length != 0 || Shields.IsOwnHost(target.Host)
            || (page.Scheme == "https" && target.Scheme != "https")
            || (!page.IsLoopback && target.IsLoopback))
            return false;

        Shields.Mode mode = Shields.ModeFor(page.Host, isPrivate: false);
        if (!Shields.Enabled || mode == Shields.Mode.Off || Shields.IsOwnHost(page.Host)) { return true; }
        FilterEngine? engine = FilterLists.Engine;
        FilterEngine.Page context = engine?.PageFor(page.AbsoluteUri) ?? new FilterEngine.Page(page.Host);
        bool strict = mode == Shields.Mode.Strict;
        if (!strict && FilterEngine.IsSameSite(target.AbsoluteUri, context)) { return true; }
        string? own = TrackerBlocker.Match(target.AbsoluteUri);
        return engine is null ? own is null
            : !engine.Match(target.AbsoluteUri, ReqType.Image, context, thirdPartyOnly: !strict, fallbackHost: own).Blocked;
    }

    internal static async Task<Result?> FetchAsync(string pageAddress, string iconAddress, CancellationToken cancel = default)
    {
        if (!Uri.TryCreate(pageAddress, UriKind.Absolute, out Uri? page)
            || !Uri.TryCreate(iconAddress, UriKind.Absolute, out Uri? current)) { return null; }
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        if (FilterLists.Engine is null && !FilterLists.Loading.IsCompleted)
            await FilterLists.Loading.WaitAsync(deadline.Token).ConfigureAwait(false);
        for (int hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!Allowed(page, current)) { return null; }
            using HttpRequestMessage request = new(HttpMethod.Get, current);
            using HttpResponseMessage response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            int status = (int)response.StatusCode;
            if (status is 301 or 302 or 303 or 307 or 308)
            {
                if (hop == MaxRedirects || response.Headers.Location is not { } location
                    || !Uri.TryCreate(current, location, out Uri? next)
                    || (current.Scheme == "https" && next.Scheme != "https")) { return null; }
                current = next;
                continue;
            }
            if (!response.IsSuccessStatusCode) { return null; }
            byte[] body = await BoundedDownload.ReadAsync(response.Content, MaxBytes, deadline.Token).ConfigureAwait(false);
            return new Result(current.AbsoluteUri, status, response.Content.Headers.ContentType?.MediaType, body);
        }
        return null;
    }
}
