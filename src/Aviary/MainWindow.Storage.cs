using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// Settings → Storage Usage, the window's half (Alpha Build 5 M4.5): measure when the page asks
/// (StorageUsage.cs does the counting, off the UI thread), and clear the engine's cached files.
/// </summary>
public sealed partial class MainWindow
{
    private bool _storageMeasuring;

    private async Task StorageMeasureAsync(CoreWebView2 page)
    {
        if (_storageMeasuring)
        {
            return;
        }

        _storageMeasuring = true;
        try
        {
            StorageUsage.Result r = await StorageUsage.MeasureAsync();
            DiagPerf("storage-usage", new { ms = r.Ms, files = r.Files, mb = Math.Round(r.Bytes / 1048576.0, 1) });

            BrowserTab? tab = TabFor(page);
            if (tab is null || !tab.IsInternalPage || tab.Title != "Settings")
            {
                return;   // bug 62: only Aviary's own Settings page hears this
            }

            var payload = new
            {
                bytes = r.Bytes,
                onDisk = r.OnDisk,
                files = r.Files,
                ms = r.Ms,
                at = r.At.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                install = r.InstallFolder,
                profile = r.ProfileFolder,
                capBytes = Diagnostics.CapBytes,

                // M4.6: what each site keeps, what is shared between them, and whether the
                // graphics caches are already promised to the next launch.
                sites = r.Sites.Select(i => new { label = i.Label, bytes = i.Bytes, files = i.Files }),
                sitesShared = r.SitesShared,
                gpuPending = AppSettings.GetBool("clearGpuCaches"),
                categories = r.Categories.Select(c => new
                {
                    key = c.Key,
                    name = c.Name,
                    plain = c.Plain,
                    bytes = c.Bytes,
                    onDisk = c.OnDisk,
                    files = c.Files,
                    biggest = c.Biggest.Select(i => new { label = i.Label, bytes = i.Bytes, files = i.Files }),
                }),
            };

            _ = page.ExecuteScriptAsync("window.__aviaryStorage && window.__aviaryStorage(" + JsonSerializer.Serialize(payload) + ")");
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "storage usage: measure");
        }
        finally
        {
            _storageMeasuring = false;
        }
    }

    /// <summary>
    /// "Clear cached files": the engine's disk cache, through the engine — it holds those files
    /// open, so deleting them from outside would fail or corrupt it. Shader caches are not
    /// included: the engine only lets them go when it starts, and the row says so.
    /// </summary>
    private async Task StorageClearCacheAsync(CoreWebView2 page)
    {
        try
        {
            CoreWebView2Profile? profile = OpenWindows.FirstOrDefault(w => !w._private)?._tabs
                .Select(t => t.View.CoreWebView2?.Profile)
                .FirstOrDefault(p => p is not null);
            if (profile is not null)
            {
                await profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);
                DiagFeature("storage-clear-cache");
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "storage usage: clear cache");
        }

        await StorageMeasureAsync(page);
    }
}
