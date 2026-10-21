using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Aviary;

internal static class DownloadChecks
{
    internal static async Task RunAsync(Action<string, bool> check)
    {
        AppSettings.Set("blockMode", "Strict");
        AppSettings.Set("customFilters", "*/blocked*$image\n@@||google-analytics.com/allowed.png$image,domain=icons.example\n");
        await FilterLists.Reload("icon-security-test");
        var page = new Uri("https://icons.example/page");
        check("Icon custom image rule enforced", !IconDownload.Allowed(page, new Uri("https://cdn.example/blocked.png")));
        check("Icon built-in tracker rule enforced", !IconDownload.Allowed(page, new Uri("https://google-analytics.com/favicon.png")));
        check("Icon targeted exception preserved", IconDownload.Allowed(page, new Uri("https://google-analytics.com/allowed.png")));
        check("Icon downgrade refused", !IconDownload.Allowed(page, new Uri("http://cdn.example/icon.png")));
        check("Icon embedded credentials refused", !IconDownload.Allowed(page, new Uri("https://user:password@cdn.example/icon.png")));
        check("Public page cannot nominate loopback icon", !IconDownload.Allowed(page, new Uri("https://127.0.0.1/icon.png")));
        check("Synthetic app icon host never fetched", !IconDownload.Allowed(page, new Uri("https://aviary.localhost/icon")));
        SiteSettings.Set(page.Host, Shields.SiteKey, "true");
        check("Icon follows explicit Shields-down choice", IconDownload.Allowed(page, new Uri("https://cdn.example/blocked.png")));
        SiteSettings.Remove(page.Host, Shields.SiteKey);

        using TcpListener portProbe = new(IPAddress.Loopback, 0); portProbe.Start();
        int port = ((IPEndPoint)portProbe.LocalEndpoint).Port; portProbe.Stop();
        string origin = $"http://127.0.0.1:{port}";
        using HttpListener listener = new(); listener.Prefixes.Add(origin + "/"); listener.Start();
        ConcurrentDictionary<string, int> hits = new();
        ConcurrentBag<Task> requests = new();
        ConcurrentBag<string> cookies = new();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int newsRequests = 0;
        async Task Respond(HttpListenerContext context)
        {
            string path = context.Request.Url!.AbsolutePath;
            hits.AddOrUpdate(path, 1, (_, old) => old + 1);
            cookies.Add(context.Request.Headers["Cookie"] ?? "");
            try
            {
                if (path is "/redirect-blocked" or "/redirect-good" or "/loop")
                {
                    context.Response.StatusCode = 302;
                    context.Response.RedirectLocation = path == "/redirect-blocked" ? "/blocked.png" : path == "/loop" ? "/loop" : "/icon.png";
                    context.Response.Headers["Set-Cookie"] = "tracking=DO_NOT_REUSE";
                }
                else if (path == "/oversize")
                {
                    context.Response.SendChunked = true;
                    await context.Response.OutputStream.WriteAsync(new byte[IconDownload.MaxBytes + 1]);
                }
                else if (path == "/stall" || (path == "/news" && Interlocked.Increment(ref newsRequests) == 1))
                {
                    context.Response.SendChunked = true;
                    await context.Response.OutputStream.WriteAsync(new byte[] { (byte)'<' });
                    await context.Response.OutputStream.FlushAsync();
                    await release.Task;
                }
                else
                {
                    byte[] body = path == "/news" ? Encoding.UTF8.GetBytes(
                        "<rss><channel><item><title>Recovered feed</title><link>https://example.test/story</link></item></channel></rss>") : new byte[] { 1, 2, 3 };
                    context.Response.ContentLength64 = body.Length;
                    await context.Response.OutputStream.WriteAsync(body);
                }
                context.Response.Close();
            }
            catch (Exception ex) when (ex is IOException or HttpListenerException or ObjectDisposedException) { }
        }
        Task serve = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                try { requests.Add(Respond(await listener.GetContextAsync())); }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { break; }
            }
        });
        try
        {
            check("Redirect into blocked icon stops before destination", await IconDownload.FetchAsync(origin + "/page", origin + "/redirect-blocked") is null
                && !hits.ContainsKey("/blocked.png"));
            var good = await IconDownload.FetchAsync(origin + "/page", origin + "/redirect-good");
            check("Allowed relative icon redirect completes", good?.Bytes.SequenceEqual(new byte[] { 1, 2, 3 }) == true && good.Url == origin + "/icon.png");
            check("Icon redirect chain is bounded", await IconDownload.FetchAsync(origin + "/page", origin + "/loop") is null && hits["/loop"] == 4);
            try { await IconDownload.FetchAsync(origin + "/page", origin + "/oversize"); check("Chunked oversized icon refused", false); }
            catch (InvalidDataException) { check("Chunked oversized icon refused", true); }
            Stopwatch elapsed = Stopwatch.StartNew();
            try { await IconDownload.FetchAsync(origin + "/page", origin + "/stall").WaitAsync(TimeSpan.FromSeconds(11)); check("Icon body has automatic deadline", false); }
            catch (OperationCanceledException) { check("Icon body has automatic deadline", elapsed.Elapsed < TimeSpan.FromSeconds(11)); }

            foreach (var feed in BirdsNews.Shipped) { AppSettings.Set(feed.SettingKey!, "false"); }
            AppSettings.Set(BirdsNews.CustomKey, origin + "/news");
            AppSettings.Set(BirdsNews.ShowKey, "true");
            elapsed.Restart();
            await BirdsNews.RefreshAsync(force: true).WaitAsync(TimeSpan.FromSeconds(19));
            using (var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(BirdsNews.Snapshot())))
            {
                var feed = snapshot.RootElement.GetProperty("feeds")[0];
                check("Stalled news body times out and reports failure", feed.GetProperty("state").GetString() == "failed"
                    && feed.GetProperty("why").GetString()!.Contains("15 seconds") && elapsed.Elapsed < TimeSpan.FromSeconds(19));
            }
            release.TrySetResult();
            await BirdsNews.RefreshAsync(force: true).WaitAsync(TimeSpan.FromSeconds(5));
            using (var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(BirdsNews.Snapshot())))
            {
                check("News refresh gate recovers after body timeout", newsRequests == 2
                    && snapshot.RootElement.GetProperty("feeds")[0].GetProperty("state").GetString() == "ok"
                    && snapshot.RootElement.GetProperty("more")[0].GetProperty("title").GetString() == "Recovered feed");
            }
            check("Background downloads never reuse redirect cookies", cookies.All(string.IsNullOrEmpty));
        }
        finally
        {
            release.TrySetResult();
            listener.Stop();
            await serve;
            await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(3));
        }
    }
}
