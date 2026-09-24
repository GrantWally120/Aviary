using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// One entry in the download list, as it is kept on disk (Alpha Build 5 M5). Everything the
/// popover needs to draw a row after Aviary has been closed and opened again — which is the
/// whole difference between a download LIST and a download menu.
/// </summary>
internal sealed class DownloadRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Where it came from. Capped, and a data: address keeps only its type (see Downloads.Start).</summary>
    public string Url { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long? TotalBytes { get; set; }
    public long ReceivedBytes { get; set; }

    /// <summary>CoreWebView2DownloadState's name: InProgress, Completed or Interrupted.</summary>
    public string State { get; set; } = "InProgress";

    /// <summary>Why it stopped: CoreWebView2DownloadInterruptReason's name, or <see cref="Downloads.AviaryClosed"/>.</summary>
    public string? Reason { get; set; }
    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedUtc { get; set; }

    /// <summary>From a private window: never written to disk, gone when the private windows are.</summary>
    [JsonIgnore]
    public bool Private { get; set; }

    /// <summary>The tab whose engine is fetching it. In WebView2 a download dies with that engine.</summary>
    [JsonIgnore]
    public int OwnerTab { get; set; }

    [JsonIgnore]
    public bool Completed => State == "Completed";

    [JsonIgnore]
    public bool Interrupted => State == "Interrupted";

    [JsonIgnore]
    public bool Running => State == "InProgress";

    /// <summary>A finished file that has since been moved or deleted, outside Aviary.</summary>
    [JsonIgnore]
    public bool Gone => Completed && (FilePath.Length == 0 || !File.Exists(FilePath));

    [JsonIgnore]
    public string Extension
    {
        get
        {
            // The name came from a website: only a plain extension is ever used for anything.
            string ext = Path.GetExtension(FileName).TrimStart('.').ToLowerInvariant();
            return ext.Length is > 0 and <= 10 && ext.All(char.IsAsciiLetterOrDigit) ? ext : string.Empty;
        }
    }
}

/// <summary>
/// A download that is happening in this process: a record and the engine's operation behind it.
/// Every touch of the operation is wrapped — once its tab's engine has gone, reading it throws.
/// </summary>
internal sealed class DownloadItem
{
    private readonly CoreWebView2DownloadOperation _operation;
    private bool _outcomeRecorded;

    public DownloadItem(CoreWebView2DownloadOperation operation, DownloadRecord record)
    {
        _operation = operation;
        Record = record;
        Pull();

        operation.StateChanged += (_, _) =>
        {
            if (_detached)
            {
                return;
            }

            Pull();
            RecordOutcome();
            Downloads.Touched(this, stateChanged: true);
        };
        operation.BytesReceivedChanged += (_, _) =>
        {
            if (_detached)
            {
                return;
            }

            Pull();
            Downloads.Touched(this, stateChanged: false);
        };
    }

    private bool _detached;
    private long _lastBytes = -1;
    private long _movedAt = System.Diagnostics.Stopwatch.GetTimestamp();

    /// <summary>
    /// The engine is between connections: the transfer dropped and Chromium is about to ask for the
    /// rest. The operation says InProgress with an interrupt reason set, and raises no event for it.
    /// The reason STAYS set on the operation that carries on (measured), so it only counts while
    /// no bytes have arrived for most of a second.
    /// </summary>
    public bool Reconnecting
    {
        get
        {
            try
            {
                return _operation.State == CoreWebView2DownloadState.InProgress
                       && _operation.InterruptReason != CoreWebView2DownloadInterruptReason.None
                       && System.Diagnostics.Stopwatch.GetElapsedTime(_movedAt).TotalMilliseconds > 700;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>The engine has set an interrupt reason on an operation it still calls InProgress.</summary>
    public bool EngineInterrupted
    {
        get
        {
            try
            {
                return _operation.State == CoreWebView2DownloadState.InProgress
                       && _operation.InterruptReason != CoreWebView2DownloadInterruptReason.None;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>Another operation carries this download on (see Downloads.Start); this one is no longer listened to.</summary>
    public void Detach() => _detached = true;

    /// <summary>
    /// Asks the engine again, for the states it does not announce (a stall, a reconnect). Returns
    /// true when anything the list shows has changed.
    /// </summary>
    public bool Refresh()
    {
        if (_detached)
        {
            return false;
        }

        string before = Record.State + "|" + Record.Reason + "|" + Record.ReceivedBytes;
        Pull();
        return before != Record.State + "|" + Record.Reason + "|" + Record.ReceivedBytes;
    }

    public DownloadRecord Record { get; }

    /// <summary>An ISO 8601 string from the engine, or null when it cannot say.</summary>
    public DateTimeOffset? EstimatedEnd
    {
        get
        {
            try
            {
                return DateTimeOffset.TryParse(_operation.EstimatedEndTime, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out DateTimeOffset end) ? end : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    public bool CanResume
    {
        get
        {
            try { return _operation.State == CoreWebView2DownloadState.Interrupted && _operation.CanResume; }
            catch (Exception) { return false; }
        }
    }

    public void Cancel()
    {
        try { _operation.Cancel(); }
        catch (Exception ex) { Diagnostics.Swallowed(ex, "download: cancel"); }
    }

    public bool Resume()
    {
        try
        {
            if (!CanResume)
            {
                return false;
            }

            _operation.Resume();
            Pull();
            Downloads.Touched(this, stateChanged: true);
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "download: resume");
            return false;
        }
    }

    /// <summary>Copies the engine's view of the download into the record.</summary>
    private void Pull()
    {
        try
        {
            CoreWebView2DownloadState state = _operation.State;
            Record.State = state.ToString();
            Record.ReceivedBytes = _operation.BytesReceived;
            if (Record.ReceivedBytes != _lastBytes)
            {
                _lastBytes = Record.ReceivedBytes;
                _movedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            }

            long? total = _operation.TotalBytesToReceive;
            Record.TotalBytes = total is > 0 ? total : null;

            string path = _operation.ResultFilePath ?? string.Empty;
            if (path.Length > 0)
            {
                Record.FilePath = path;
                Record.FileName = Path.GetFileName(path);
            }

            if (state == CoreWebView2DownloadState.InProgress)
            {
                // Still running, but the connection dropped and the engine is reconnecting.
                Record.Reason = Reconnecting ? _operation.InterruptReason.ToString() : null;
                Record.FinishedUtc = null;
            }
            else
            {
                Record.Reason = state == CoreWebView2DownloadState.Interrupted ? _operation.InterruptReason.ToString() : null;
                Record.FinishedUtc ??= DateTimeOffset.UtcNow;
            }
        }
        catch (Exception)
        {
            // The engine has let go of it. What the record last said stands.
        }
    }

    /// <summary>Diagnostics category 15: how the download ended — once, when it does. Never for a private one.</summary>
    private void RecordOutcome()
    {
        if (_outcomeRecorded || Record.Private || !Diagnostics.On || Record.Running)
        {
            return;
        }

        _outcomeRecorded = true;
        Diagnostics.Download("end", new
        {
            file = Record.FileName,
            url = Record.Url,
            result = Record.State,
            reason = Record.Reason,
            bytes = Record.ReceivedBytes,
            totalBytes = Record.TotalBytes,
        });
    }
}

/// <summary>
/// The download list (Alpha Build 5 M5) — Safari's, kept in <c>downloads.json</c> so it is still
/// there tomorrow, and emptied by Safari's own rule, "Remove download list items": after one day
/// (Safari's default), when Aviary quits, as soon as a download succeeds, or only by hand.
///
/// <para><b>What is kept is the LIST, never the files.</b> Removing a row, clearing the list or
/// the list expiring never touches a file on disk — Safari's rule too.</para>
///
/// <para><b>Private windows</b> get a list of their own that is never written anywhere and goes
/// when the last private window closes. The files themselves stay, as in every browser: a
/// private window forgets where you went, not what you chose to keep.</para>
/// </summary>
internal static class Downloads
{
    /// <summary>The reason given to a download that was running when Aviary last closed.</summary>
    public const string AviaryClosed = "AviaryClosed";

    /// <summary>The reason given to a download whose tab's engine closed under it.</summary>
    public const string TabClosed = "TabClosed";

    public const string PolicyKey = "downloadHistory";
    public const string AfterOneDay = "After one day";
    public const string WhenAviaryQuits = "When Aviary quits";
    public const string UponSuccess = "Upon successful download";
    public const string Manually = "Manually";

    public static readonly string[] Policies = { AfterOneDay, WhenAviaryQuits, UponSuccess, Manually };

    private static readonly object Gate = new();
    private static List<DownloadRecord>? _saved;
    private static readonly List<DownloadRecord> PrivateList = new();
    private static readonly Dictionary<string, DownloadItem> Live = new();
    private static Timer? _saveTimer;

    /// <summary>Anything about the list changed. Raised on the thread the engine raised it on (the UI thread).</summary>
    public static event Action? Changed;

    /// <summary>Only a download's bytes moved. Separate, because it fires many times a second.</summary>
    public static event Action<DownloadRecord>? Progressed;

    private static string FilePath => AppPaths.Of("downloads.json");

    public static string Policy
    {
        get
        {
            string p = AppSettings.Get(PolicyKey);
            return Policies.FirstOrDefault(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase)) ?? AfterOneDay;
        }
    }

    private static List<DownloadRecord> Saved
    {
        get
        {
            lock (Gate)
            {
                if (_saved is not null)
                {
                    return _saved;
                }

                _saved = new List<DownloadRecord>();
                try
                {
                    if (File.Exists(FilePath))
                    {
                        _saved = JsonSerializer.Deserialize<List<DownloadRecord>>(File.ReadAllText(FilePath)) ?? new();
                    }
                }
                catch (Exception ex)
                {
                    _saved = new List<DownloadRecord>();
                    Diagnostics.Swallowed(ex, "downloads: load downloads.json");
                }

                // A download that was running when Aviary last closed did not survive it: the engine
                // that was fetching it has gone. It is shown as stopped, and can be tried again.
                bool changed = false;
                foreach (DownloadRecord r in _saved.Where(r => r.Running))
                {
                    r.State = "Interrupted";
                    r.Reason = AviaryClosed;
                    r.FinishedUtc ??= DateTimeOffset.UtcNow;
                    changed = true;
                }

                // "When Aviary quits" whose quit never ran (a crash, a power cut): it runs now.
                if (Policy == WhenAviaryQuits)
                {
                    changed |= _saved.RemoveAll(r => !r.Running) > 0;
                }

                if (changed)
                {
                    ScheduleSave(immediate: true);
                }

                return _saved;
            }
        }
    }

    /// <summary>The list a window of this kind shows, newest first, after the retention rule has run.</summary>
    public static List<DownloadRecord> List(bool isPrivate)
    {
        Prune();
        lock (Gate)
        {
            return (isPrivate ? PrivateList : Saved).OrderByDescending(r => r.StartedUtc).ToList();
        }
    }

    public static bool Any(bool isPrivate)
    {
        lock (Gate)
        {
            return (isPrivate ? PrivateList : Saved).Count > 0;
        }
    }

    public static DownloadItem? LiveFor(DownloadRecord record)
    {
        lock (Gate)
        {
            return Live.TryGetValue(record.Id, out DownloadItem? item) ? item : null;
        }
    }

    /// <summary>The downloads of this kind that are moving right now.</summary>
    public static List<DownloadItem> Active(bool isPrivate)
    {
        lock (Gate)
        {
            return Live.Values.Where(i => i.Record.Private == isPrivate && i.Record.Running).ToList();
        }
    }

    /// <summary>A download has started in a tab of a window of this kind.</summary>
    public static DownloadItem Start(CoreWebView2DownloadOperation operation, bool isPrivate, int ownerTab)
    {
        string url = string.Empty;
        string mime = string.Empty;
        try
        {
            url = operation.Uri ?? string.Empty;
            mime = operation.MimeType ?? string.Empty;
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "downloads: read the new download");
        }

        // A data: address IS the file; keeping it would put the whole download into the list.
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            int comma = url.IndexOf(',');
            url = comma > 0 ? url.Substring(0, Math.Min(comma, 100)) : "data:";
        }
        else if (url.Length > 2048)
        {
            url = url.Substring(0, 2048);
        }

        // Chromium resumes a download whose connection dropped by itself, a couple of seconds later -
        // and WebView2 reports the resumed transfer as a NEW download, with a new operation and a
        // new file name, while the old operation goes quiet for good (measured: State still
        // InProgress, InterruptReason set, bytes frozen). That new download IS the old one. It takes
        // over the old row rather than becoming a second one beside a row that never finishes.
        // Told apart from the same link simply clicked twice by where it STARTS: a new download
        // starts at nothing, a continuation at the bytes already fetched.
        long startsAt = 0;
        try { startsAt = operation.BytesReceived; } catch (Exception) { }

        DownloadItem? stalled = null;
        if (startsAt > 0)
        {
            lock (Gate)
            {
                stalled = Live.Values.FirstOrDefault(i => i.Record.Running && i.Record.Private == isPrivate
                                                          && i.Record.Url == url && i.EngineInterrupted);
            }
        }

        if (stalled is not null)
        {
            stalled.Detach();
            DownloadRecord carried = stalled.Record;
            carried.OwnerTab = ownerTab;
            DownloadItem continued = new(operation, carried);
            lock (Gate)
            {
                Live[carried.Id] = continued;
            }

            if (!isPrivate)
            {
                Diagnostics.Download("reconnected", new { file = carried.FileName, url = carried.Url, bytes = carried.ReceivedBytes, startsAt });
                ScheduleSave(immediate: true);
            }

            Changed?.Invoke();
            return continued;
        }

        DownloadRecord record = new() { Url = url, MimeType = mime, Private = isPrivate, OwnerTab = ownerTab };
        DownloadItem item = new(operation, record);

        lock (Gate)
        {
            (isPrivate ? PrivateList : Saved).Add(record);
            Live[record.Id] = item;
        }

        if (!isPrivate)
        {
            ScheduleSave(immediate: true);
        }

        Changed?.Invoke();
        return item;
    }

    /// <summary>
    /// Once a second while anything runs (the ring's tick): the engine announces bytes and endings,
    /// but not a stall or a reconnect, so each running download is asked directly.
    /// </summary>
    public static void Poll()
    {
        List<DownloadItem> running;
        lock (Gate)
        {
            running = Live.Values.Where(i => i.Record.Running).ToList();
        }

        foreach (DownloadItem item in running)
        {
            if (item.Refresh())
            {
                Progressed?.Invoke(item.Record);
            }
        }
    }

    /// <summary>How many downloads this tab's engine is still fetching - a tab with any is parked, not closed.</summary>
    public static int RunningFor(int tabId)
    {
        lock (Gate)
        {
            return Live.Values.Count(i => i.Record.OwnerTab == tabId && i.Record.Running);
        }
    }

    /// <summary>
    /// A tab's engine has gone: whatever it was still fetching has stopped, and the engine will
    /// never say so (measured: the operation just goes quiet). The list says it instead.
    /// </summary>
    public static void OwnerGone(int tabId)
    {
        List<DownloadRecord> stopped;
        lock (Gate)
        {
            stopped = Live.Values.Where(i => i.Record.OwnerTab == tabId).Select(i => i.Record).ToList();
            foreach (DownloadRecord r in stopped)
            {
                Live.Remove(r.Id);
                if (r.Running)
                {
                    r.State = "Interrupted";
                    r.Reason = TabClosed;
                    r.FinishedUtc ??= DateTimeOffset.UtcNow;
                }
            }
        }

        if (stopped.Count > 0)
        {
            ScheduleSave(immediate: true);
            Changed?.Invoke();
        }
    }

    /// <summary>From a <see cref="DownloadItem"/>: the engine said something about it.</summary>
    internal static void Touched(DownloadItem item, bool stateChanged)
    {
        DownloadRecord record = item.Record;

        if (!stateChanged)
        {
            if (!record.Private)
            {
                ScheduleSave(immediate: false);
            }

            Progressed?.Invoke(record);
            return;
        }

        // "Upon successful download": gone from the list the moment it lands — Safari's rule.
        if (record.Completed && Policy == UponSuccess)
        {
            lock (Gate)
            {
                (record.Private ? PrivateList : Saved).Remove(record);
            }
        }

        if (!record.Running)
        {
            lock (Gate)
            {
                // A stopped download can be resumed, so its operation is kept while it can be.
                if (record.Completed || !item.CanResume)
                {
                    Live.Remove(record.Id);
                }
            }
        }
        else
        {
            lock (Gate)
            {
                Live[record.Id] = item;
            }
        }

        if (!record.Private)
        {
            ScheduleSave(immediate: true);
        }

        Changed?.Invoke();
    }

    /// <summary>Takes one row off the list. The file is not touched.</summary>
    public static void Remove(DownloadRecord record)
    {
        bool removed;
        lock (Gate)
        {
            removed = (record.Private ? PrivateList : Saved).Remove(record);
        }

        if (removed)
        {
            if (!record.Private)
            {
                ScheduleSave(immediate: true);
            }

            Changed?.Invoke();
        }
    }

    /// <summary>Empties the list of everything that has finished. What is still downloading stays. Returns how many went.</summary>
    public static int Clear(bool isPrivate)
    {
        int removed;
        lock (Gate)
        {
            removed = (isPrivate ? PrivateList : Saved).RemoveAll(r => !r.Running);
        }

        if (removed > 0)
        {
            if (!isPrivate)
            {
                ScheduleSave(immediate: true);
            }

            Changed?.Invoke();
        }

        return removed;
    }

    /// <summary>
    /// "After one day": anything that finished more than a day ago leaves the list. Run whenever the
    /// list is about to be shown, so an expired row is never drawn, rather than on a timer of its own.
    /// </summary>
    public static void Prune()
    {
        if (Policy != AfterOneDay)
        {
            return;
        }

        DateTimeOffset cutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(1);
        int removed;
        lock (Gate)
        {
            removed = Saved.RemoveAll(r => !r.Running && (r.FinishedUtc ?? r.StartedUtc) < cutoff);
            removed += PrivateList.RemoveAll(r => !r.Running && (r.FinishedUtc ?? r.StartedUtc) < cutoff);
        }

        if (removed > 0)
        {
            ScheduleSave(immediate: true);
            Changed?.Invoke();
        }
    }

    /// <summary>The rule changed in Settings: apply what it means now.</summary>
    public static void PolicyChanged()
    {
        if (Policy == UponSuccess)
        {
            lock (Gate)
            {
                Saved.RemoveAll(r => r.Completed);
                PrivateList.RemoveAll(r => r.Completed);
            }

            ScheduleSave(immediate: true);
            Changed?.Invoke();
            return;
        }

        Prune();
    }

    /// <summary>The last private window has closed: its list goes with it.</summary>
    public static void ForgetPrivate()
    {
        lock (Gate)
        {
            PrivateList.Clear();
            foreach (string id in Live.Where(p => p.Value.Record.Private).Select(p => p.Key).ToList())
            {
                Live.Remove(id);
            }
        }

        Changed?.Invoke();
    }

    /// <summary>Aviary is closing: "When Aviary quits" runs, and the list is written one last time.</summary>
    public static void OnQuit()
    {
        try
        {
            lock (Gate)
            {
                if (Policy == WhenAviaryQuits)
                {
                    Saved.RemoveAll(r => !r.Running);
                }

                foreach (DownloadRecord r in Saved.Where(r => r.Running))
                {
                    r.State = "Interrupted";
                    r.Reason = AviaryClosed;
                    r.FinishedUtc ??= DateTimeOffset.UtcNow;
                }
            }

            SaveNow();
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "downloads: on quit");
        }
    }

    /// <summary>
    /// Written temp-then-move. While bytes are arriving the write waits two seconds, because progress
    /// fires many times a second and the disk does not need to hear about every one of them; a
    /// download starting, stopping or finishing is written at once.
    /// </summary>
    private static void ScheduleSave(bool immediate)
    {
        if (immediate)
        {
            SaveNow();
            return;
        }

        lock (Gate)
        {
            _saveTimer ??= new Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);
            _saveTimer.Change(2000, Timeout.Infinite);
        }
    }

    private static void SaveNow()
    {
        try
        {
            string json;
            lock (Gate)
            {
                _saveTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                json = JsonSerializer.Serialize(Saved, new JsonSerializerOptions { WriteIndented = true });
            }

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "downloads: save downloads.json");
        }
    }

    // ── What a row says ────────────────────────────────────────────────────────

    /// <summary>
    /// Decimal units, as Apple has used since Snow Leopard: a file the server calls 100 MB is shown
    /// as 100 MB, not 95.4.
    /// </summary>
    public static string Size(long bytes)
    {
        if (bytes <= 0)
        {
            return "Zero bytes";
        }

        if (bytes < 1000)
        {
            return bytes + (bytes == 1 ? " byte" : " bytes");
        }

        string[] units = { "KB", "MB", "GB", "TB" };
        double value = bytes / 1000.0;
        int unit = 0;
        while (value >= 1000 && unit < units.Length - 1)
        {
            value /= 1000;
            unit++;
        }

        string number = value >= 100 || unit == 0
            ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.#", CultureInfo.InvariantCulture);
        return number + " " + units[unit];
    }

    /// <summary>Safari's own words for the time left.</summary>
    public static string Remaining(DateTimeOffset? end)
    {
        if (end is not DateTimeOffset e)
        {
            return "Downloading…";
        }

        double seconds = (e - DateTimeOffset.Now).TotalSeconds;
        if (seconds < 1.5)
        {
            return "Almost done";
        }

        if (seconds < 60)
        {
            int s = (int)Math.Round(seconds);
            return s + (s == 1 ? " second remaining" : " seconds remaining");
        }

        if (seconds < 3600)
        {
            int m = (int)Math.Round(seconds / 60);
            return "About " + m + (m == 1 ? " minute remaining" : " minutes remaining");
        }

        int h = (int)Math.Round(seconds / 3600);
        return "About " + h + (h == 1 ? " hour remaining" : " hours remaining");
    }

    /// <summary>The second line of a row.</summary>
    public static string Status(DownloadRecord r, DownloadItem? live)
    {
        if (r.Running && r.Reason is not null)
        {
            return "Reconnecting — " + Size(r.ReceivedBytes) + (r.TotalBytes is long all ? " of " + Size(all) : "");
        }

        if (r.Running)
        {
            string got = Size(r.ReceivedBytes);
            return r.TotalBytes is long total
                ? got + " of " + Size(total) + " — " + Remaining(live?.EstimatedEnd)
                : got + " — size not given";
        }

        if (r.Completed)
        {
            return r.Gone ? "Removed" : Size(r.TotalBytes ?? r.ReceivedBytes);
        }

        return (r.Reason ?? string.Empty) switch
        {
            "UserCanceled" => "Stopped",
            AviaryClosed or "UserShutdown" => "Stopped — Aviary was closed",
            TabClosed => "Stopped — its window was closed",
            "NetworkFailed" or "NetworkTimeout" or "NetworkDisconnected" or "NetworkServerDown" => "Failed — the connection was lost",
            "ServerNoRange" or "ServerFailed" or "ServerCertificateProblem" or "ServerUnexpectedResponse" => "Failed — the site stopped sending it",
            "ServerBadContent" or "ServerUnauthorized" or "ServerForbidden" or "ServerCrossOriginRedirect" => "Failed — the site refused it",
            "FileNoSpace" => "Failed — the disk is full",
            "FileAccessDenied" or "FileNameTooLong" or "FileTooLarge" or "FileTransientError" or "FileFailed" => "Failed — it couldn’t be saved there",
            "FileBlockedByPolicy" or "FileSecurityCheckFailed" or "FileMalicious" or "FileVirusInfected" => "Blocked — Windows security stopped this file",
            _ => "Stopped",
        };
    }

    /// <summary>
    /// Kinds of file that RUN rather than open (M5, the "Ask before opening a downloaded program"
    /// row). Windows has no Gatekeeper; this is the question Safari's asks, asked by the browser.
    /// </summary>
    private static readonly HashSet<string> Programs = new(StringComparer.OrdinalIgnoreCase)
    {
        "exe", "msi", "msix", "msixbundle", "appx", "appxbundle", "appinstaller", "application", "appref-ms",
        "bat", "cmd", "com", "pif", "scr", "cpl", "msc", "hta", "reg", "lnk", "url",
        "ps1", "psm1", "vbs", "vbe", "js", "jse", "wsf", "wsh", "jar",
        "iso", "img", "vhd", "vhdx",
    };

    public static bool IsProgram(DownloadRecord r) => r.Extension.Length > 0 && Programs.Contains(r.Extension);

    /// <summary>
    /// Shows the file in Explorer, selected. A file that has moved on shows its folder; a folder that
    /// has gone too shows the Downloads folder — never nothing.
    /// </summary>
    public static void Reveal(DownloadRecord r)
    {
        try
        {
            if (r.FilePath.Length > 0 && File.Exists(r.FilePath))
            {
                Shell("explorer.exe", "/select,\"" + r.FilePath + "\"");
                return;
            }

            string folder = r.FilePath.Length > 0 ? Path.GetDirectoryName(r.FilePath) ?? string.Empty : string.Empty;
            Shell("explorer.exe", "\"" + (folder.Length > 0 && Directory.Exists(folder) ? folder : KnownFolders.Downloads) + "\"");
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "download: show in folder");
        }
    }

    /// <summary>The folder downloads go to now.</summary>
    public static void RevealFolder()
    {
        try
        {
            string dir = AppSettings.Get("downloadDir");
            Shell("explorer.exe", "\"" + (dir.Length > 0 && Directory.Exists(dir) ? dir : KnownFolders.Downloads) + "\"");
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "download: open the downloads folder");
        }
    }

    /// <summary>Opens a finished file with whatever Windows opens that kind of file with.</summary>
    public static bool Open(DownloadRecord r)
    {
        try
        {
            if (!r.Completed || r.Gone)
            {
                return false;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = r.FilePath,
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception ex)
        {
            // "No app is associated", or the person cancelled Windows' own warning.
            Diagnostics.Swallowed(ex, "download: open");
            return false;
        }
    }

    private static void Shell(string file, string arguments)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = file,
            Arguments = arguments,
            UseShellExecute = true,
        });
    }
}
