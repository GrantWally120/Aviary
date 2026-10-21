using System.Runtime.InteropServices;
using Aviary;
using Microsoft.Win32.SafeHandles;

// Exercises the actual shipped scanner with isolated files. Run from the repository root:
// dotnet run --project tools/test/storage -c Release --no-restore
// No normal-profile reads or writes. No test packages or native desktop input.
string workspace = Path.GetFullPath(Directory.GetCurrentDirectory());
if (!File.Exists(Path.Combine(workspace, "AB5-CONTINUE.md"))) throw new InvalidOperationException("Run from the Aviary workspace.");
string artifact = Path.Combine(workspace, ".artifact");
string fixtures = Path.Combine(artifact, "storage-fixtures-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixtures);
int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    Console.WriteLine("PASS " + name);
    passed++;
}
(string Install, string Profile) Pair(string name)
{
    string install = Path.Combine(fixtures, name, "install"), profile = Path.Combine(fixtures, name, "profile");
    Directory.CreateDirectory(install);
    Directory.CreateDirectory(profile);
    return (install, profile);
}
void Write(string root, string relative, int bytes)
{
    string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, new byte[bytes]);
}
StorageUsage.Category Category(StorageUsage.Result result, string key) => result.Categories.Single(c => c.Key == key);
try
{
    var paths = Pair("basic");
    Write(paths.Install, "Aviary.dll", 301);
    Write(paths.Install, "Library/files/project.html", 107);
    Write(paths.Profile, "history.json", 211);
    Write(paths.Profile, "user.data.improvementcollection/issues.json", 37);
    Write(paths.Profile, "WebView2/EBWebView/Default/Cache/Cache_Data/data_0", 4097);
    Write(paths.Profile, "WebView2/EBWebView/Default/IndexedDB/https_foo_bar.example_0.indexeddb.leveldb/000001.log", 131);
    Write(paths.Profile, "WebView2/EBWebView/Default/Local Storage/leveldb/000003.log", 53);
    Write(paths.Profile, "WebView2/EBWebView/Default/Network/Cookies-wal", 17);
    Write(paths.Profile, "WebView2/EBWebView/Default/IndexedDB/https_guess.example.indexeddb.leveldb/000001.log", 19);
    var result = await StorageUsage.MeasureAsync(paths.Install, paths.Profile);
    Check(result.Bytes == 4973 && result.Files == 9 && result.Complete, "all fixture files measured once, categories reconcile");
    Check(Category(result, "collection").Bytes == 107 && Category(result, "yours").Bytes == 211
        && Category(result, "diagnostics").Bytes == 37 && Category(result, "cache").Bytes == 4097, "installation and profile categories remain distinct");
    Check(result.Sites.Single().Label == "foo_bar.example" && result.Sites.Single().Bytes == 131, "origin parser preserves underscores and rejects ambiguous origin folders");
    Check(Category(result, "sites").Bytes == result.Sites.Sum(s => s.Bytes) + result.SitesShared + result.SitesOtherBytes
        && result.SitesShared == 89, "site, shared and omitted totals exactly match website-data category");
    Check(result.StartedAt <= result.At && result.Ms >= 0 && result.SkippedEntries == 0, "snapshot timing and completeness metadata are available");

    Write(paths.Profile, "history.json", 401);
    var changed = await StorageUsage.MeasureAsync(paths.Install, paths.Profile);
    Check(changed.Bytes == result.Bytes + 190, "next scan observes changed file length");
    for (int i = 0; i < 14; i++) Write(paths.Profile, $"WebView2/EBWebView/Default/IndexedDB/https_site{i}.example_0.indexeddb.leveldb/file", i + 1);
    var manySites = await StorageUsage.MeasureAsync(paths.Install, paths.Profile);
    Check(manySites.SiteCount == 15 && manySites.Sites.Count == 12 && manySites.SitesOtherBytes > 0
        && manySites.Sites.Sum(s => s.Bytes) + manySites.SitesShared + manySites.SitesOtherBytes == Category(manySites, "sites").Bytes,
        "top-site limit exposes unlisted bytes rather than losing them");
    Write(paths.Profile, "webview2/ebwebview/Default/code cache/js/file", 73);
    var caseInsensitive = await StorageUsage.MeasureAsync(paths.Install, paths.Profile);
    Check(Category(caseInsensitive, "cache").Bytes == 4170, "engine folder classification follows Windows case-insensitive paths");

    var moving = Pair("moving");
    string livePath = Path.Combine(moving.Profile, "WebView2/EBWebView/Default/IndexedDB/https_live.example_0.indexeddb.leveldb/live");
    Directory.CreateDirectory(Path.GetDirectoryName(livePath)!);
    using (var writerStop = new CancellationTokenSource())
    {
        var writer = Task.Run(() =>
        {
            using var stream = new FileStream(livePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            while (!writerStop.IsCancellationRequested) { stream.SetLength(17); stream.SetLength(9001); }
        });
        try
        {
            bool reconciled = true;
            for (int i = 0; i < 15; i++)
            {
                var current = await StorageUsage.MeasureAsync(moving.Install, moving.Profile);
                reconciled &= current.Sites.Sum(s => s.Bytes) + current.SitesShared + current.SitesOtherBytes == Category(current, "sites").Bytes;
            }
            Check(reconciled, "live-changing database uses one byte sample for category and site totals");
        }
        finally { writerStop.Cancel(); await writer; }
    }

    var overlap = Pair("overlap");
    string nested = Path.Combine(overlap.Install, "profile");
    Write(overlap.Install, "Aviary.dll", 11);
    Write(nested, "history.json", 23);
    var nestedProfile = await StorageUsage.MeasureAsync(overlap.Install, nested);
    Check(nestedProfile.Files == 2 && nestedProfile.Bytes == 34 && Category(nestedProfile, "yours").Bytes == 23,
        "profile inside installation is neither double counted nor classified as app");
    string nestedInstall = Path.Combine(overlap.Profile, "app");
    Write(overlap.Profile, "settings.json", 29);
    Write(nestedInstall, "Aviary.dll", 31);
    var nestedApp = await StorageUsage.MeasureAsync(nestedInstall, overlap.Profile);
    Check(nestedApp.Files == 2 && nestedApp.Bytes == 60 && Category(nestedApp, "app").Bytes == 31,
        "installation inside profile is measured as app once");
    var same = await StorageUsage.MeasureAsync(nestedInstall, nestedInstall);
    Check(same.Files == 1 && same.Bytes == 31, "identical roots are enumerated once");
    var missing = await StorageUsage.MeasureAsync(Path.Combine(fixtures, "missing"), overlap.Profile);
    Check(!missing.Complete && missing.SkippedDirectories == 1 && missing.Bytes == 60,
        "missing/inaccessible root is reported as partial without losing the readable root");
    using (var cancelled = new CancellationTokenSource())
    {
        cancelled.Cancel();
        bool cancelledObserved = false;
        try { await StorageUsage.MeasureAsync(paths.Install, paths.Profile, cancelled.Token); }
        catch (OperationCanceledException) { cancelledObserved = true; }
        Check(cancelledObserved, "cancelled measurement does not return an apparently complete result");
    }

    if (OperatingSystem.IsWindows())
    {
        var disk = Pair("disk");
        Write(disk.Install, "resident.dat", 1);
        string sparse = Path.Combine(disk.Profile, "sparse.bin");
        using (var stream = new FileStream(sparse, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
        {
            if (!Native.DeviceIoControl(stream.SafeFileHandle, 0x000900C4 /* FSCTL_SET_SPARSE */, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            stream.SetLength(4 * 1024 * 1024);
        }
        var sparseResult = await StorageUsage.MeasureAsync(disk.Install, disk.Profile);
        Check(sparseResult.DiskEstimatedFiles == 0 && sparseResult.Bytes == 4 * 1024 * 1024 + 1 && sparseResult.OnDisk < sparseResult.Bytes,
            "sparse/resident file allocation comes from Windows, without cluster-size guesses");

        string original = Path.Combine(disk.Install, "linked.dat"), link = Path.Combine(disk.Profile, "linked-copy.dat");
        Write(disk.Install, "linked.dat", 8192);
        if (!Native.CreateHardLinkW(link, original, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var linked = await StorageUsage.MeasureAsync(disk.Install, disk.Profile);
        Check(linked.HardLinkDuplicates == 1 && linked.Bytes == sparseResult.Bytes + 16384
            && linked.OnDisk == sparseResult.OnDisk + 8192, "hard-linked paths retain logical lengths while disk allocation is counted once");

        // Junctions do not require Developer Mode or symlink privilege. Create one in the
        // fixture only, aimed at another fixture folder, and verify that it is not followed.
        string target = Path.Combine(fixtures, "junction-target");
        Directory.CreateDirectory(target);
        Write(target, "outside.dat", 16000);
        string junction = Path.Combine(disk.Profile, "outside-link");
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            Arguments = "/d /c mklink /J \"" + junction + "\" \"" + target + "\"",
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        })!;
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException("Could not create isolated junction: " + await process.StandardError.ReadToEndAsync());
        var junctionResult = await StorageUsage.MeasureAsync(disk.Install, disk.Profile);
        Check(junctionResult.ReparsePointsSkipped == 1 && junctionResult.Bytes == linked.Bytes,
            "junction is explicitly excluded and cannot inflate measurement with external files");
        Directory.Delete(junction); // remove only the junction, before recursive fixture cleanup
    }

    Environment.SetEnvironmentVariable("AVIARY_PROFILE_DIR", paths.Profile);
    Task<StorageUsage.Result> first = StorageUsage.MeasureAsync(), second = StorageUsage.MeasureAsync();
    Check(ReferenceEquals(first, second), "simultaneous callers share the in-flight scan");
    await Task.WhenAll(first, second);
    Console.WriteLine($"Storage scanner: {passed} passed.");
}
finally
{
    // Only remove the GUID fixture tree created above, and validate the absolute boundary.
    string full = Path.GetFullPath(fixtures);
    if (!full.StartsWith(Path.GetFullPath(artifact) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Refusing cleanup outside workspace artifact folder.");
    Directory.Delete(full, recursive: true);
}

internal static class Native
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeviceIoControl(SafeFileHandle handle, uint code, IntPtr input, uint inputSize,
        IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CreateHardLinkW(string newName, string existingName, IntPtr security);
}
