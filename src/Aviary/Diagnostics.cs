using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;

namespace Aviary;

/// <summary>
/// Local diagnostics: the "Allow data collection for improvements" switch, and the folder it
/// fills — %LOCALAPPDATA%\Aviary\user.data.improvementcollection\.
///
/// WHAT and WHY were agreed with Grant and are written down in PHASE5-PLAN.md (M1): gather
/// everything — every address, title and search included — keep it on this machine only, and
/// never gather a secret. One switch, all or nothing, off until it is switched on.
///
/// HOW, in one paragraph. Callers anywhere (mostly the UI thread) hand a small record to a
/// bounded channel and return; nothing on the UI thread ever waits on the disk. ONE writer
/// thread turns each record into a JSON line, runs EVERY string in it through
/// <see cref="Scrub"/> — the secret filter, in Diagnostics.Scrub.cs — and appends it to its
/// category's file. The writer also owns rotation, the 200 MB cap, 30-day retention,
/// deletion and summary.md, so the files are only ever touched from one thread and there is
/// no locking around them at all.
///
/// While the switch is off, every entry point is one volatile read and a return. That is
/// what lets the 71 formerly-silent catch blocks call <see cref="Swallowed"/> freely.
///
/// There is deliberately NO network code anywhere in this feature. Nothing here can send.
/// </summary>
internal static partial class Diagnostics
{
    /// <summary>The settings key behind the switch at the bottom of Privacy.</summary>
    public const string SettingKey = "improvementCollection";

    public const long CapBytes = 200L * 1024 * 1024;
    public const int RetentionDays = 30;

    /// <summary>A category file is moved into archive/ once it reaches this size.</summary>
    private const long RotateBytes = 16L * 1024 * 1024;

    /// <summary>
    /// How many records may wait for the writer. A page that fires thousands of requests a
    /// second must not be able to grow Aviary's memory without bound; past this, records are
    /// dropped and COUNTED, and the count is reported in summary.md.
    /// </summary>
    private const int ChannelCapacity = 20_000;

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Aviary", "user.data.improvementcollection");

    private static string CrashFolder => Path.Combine(Folder, "crashes");
    private static string ArchiveFolder => Path.Combine(Folder, "archive");

    private enum LogFile
    {
        Browsing,
        Network,
        Blocking,
        Events,
        Pages,
        Issues,
        Perf,
    }

    private static readonly string[] FileNames =
    {
        "browsing.jsonl", "network.jsonl", "blocking.jsonl", "events.jsonl",
        "pages.jsonl", "issues.jsonl", "perf.jsonl",
    };

    // ══ STATE ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 0 = not decided yet (the first instants of start-up, before settings are read: records
    /// are HELD, not dropped, so a failure while loading settings is still seen), 1 = on,
    /// 2 = off.
    /// </summary>
    private static volatile int _mode;

    /// <summary>True while collecting. Callers use it to skip building a record at all.</summary>
    public static bool On => _mode == 1;

    /// <summary>
    /// Private browsing is never recorded. There is no private window until M2; the check is
    /// here from the start — keyed on the same setting History.Record already honours — so the
    /// call sites never have to remember it.
    /// </summary>
    private static volatile bool _private;

    /// <summary>On, and not private. Browsing, network, blocking and page errors check this.</summary>
    public static bool RecordsBrowsing => _mode == 1 && !_private;

    /// <summary>Raised after collection starts or stops, on the thread that flipped it.</summary>
    public static event Action<bool>? EnabledChanged;

    private static readonly object ToggleGate = new();

    private static string _session = "--------";
    private static DateTimeOffset _sessionStart;
    private static string _sessionReason = "launch";

    private static readonly DateTimeOffset ProcessStart = ReadProcessStart();

    // ══ THE CHANNEL ═════════════════════════════════════════════════════════

    private sealed class Entry
    {
        public LogFile File;
        public DateTimeOffset Time;
        public string Kind = string.Empty;
        public string? Name;
        public object? Data;

        /// <summary>
        /// The collection session this belongs to, taken when it HAPPENED. Read at write time
        /// instead, the last lines of a session switched off and straight back on were
        /// labelled with the new session. Null for start-up records held before any session
        /// existed; they take the session they are written into.
        /// </summary>
        public string? Session;

        /// <summary>Not a record: work the writer runs in order with the records around it.</summary>
        public Action? Command;
    }

    private static readonly Channel<Entry> Queue = Channel.CreateBounded<Entry>(
        new BoundedChannelOptions(ChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });

    private static long _dropped;
    private static long _writeErrors;
    private static long _linesWritten;
    private static int _writerStarted;

    [ThreadStatic]
    private static bool _onWriter;

    private static readonly JsonSerializerOptions DataOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Relaxed escaping on purpose: these files are read by a person, and "—" or "•••" should
    /// read as themselves rather than as —. Nothing here is ever embedded in HTML.
    /// </summary>
    private static readonly JsonWriterOptions LineOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    private static readonly JsonWriterOptions DocumentOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = true,
    };

    // ══ LIFECYCLE ═══════════════════════════════════════════════════════════

    /// <summary>
    /// First thing in App's constructor. Starts the writer and decides, from settings, whether
    /// this launch collects. Also hooks the two process-level failure events that WinUI's own
    /// UnhandledException never sees.
    /// </summary>
    public static void Initialise()
    {
        Mark("app-start");
        StartWriter();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Crash(ex, "background thread", e.IsTerminating);
            }
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Issue("unobserved-task", null, new
            {
                exception = DescribeException(e.Exception),
            });
            e.SetObserved();
        };

        _private = AppSettings.GetBool("privateMode");
        SetEnabled(AppSettings.GetBool(SettingKey), "launch");
    }

    /// <summary>The settings switch. Safe to call repeatedly with the same value.</summary>
    public static void SetEnabled(bool on) => SetEnabled(on, on ? "switched on" : "switched off");

    private static void SetEnabled(bool on, string reason)
    {
        bool changed;

        lock (ToggleGate)
        {
            int mode = _mode;
            if ((on && mode == 1) || (!on && mode == 2))
            {
                return;
            }

            if (on)
            {
                _session = Guid.NewGuid().ToString("N").Substring(0, 8);
                _sessionStart = DateTimeOffset.Now;
                _sessionReason = reason;
                ResetSessionCounters();
                _mode = 1;

                // Switched on from Settings, so on the UI thread: describe the window as it is
                // NOW. Otherwise session.json carries the description taken at start-up, before
                // the window had even been maximised.
                if (_ui?.HasThreadAccess == true)
                {
                    RefreshWindow();
                }

                Entry start = new()
                {
                    File = LogFile.Events,
                    Time = DateTimeOffset.Now,
                    Session = _session,
                    Kind = "session",
                    Name = "start",
                    Data = new
                    {
                        reason,
                        version = BuildInfo.Version,
                        channel = BuildInfo.Channel,
                        pid = Environment.ProcessId,
                        settings = AppSettings.Snapshot(),
                    },
                };

                EnqueueCommand(() => OpenOnWriter(start));
                StartTimers();
            }
            else
            {
                if (mode == 1)
                {
                    Enqueue(LogFile.Events, "session", "stop", new { reason });
                    EnqueueCommand(() => CloseOnWriter(reason));
                }
                else
                {
                    // Never switched on this launch: anything held during start-up goes.
                    EnqueueCommand(() => Held.Clear());
                }

                _mode = 2;
                StopTimers();
            }

            changed = mode != 0 || on;
        }

        if (changed)
        {
            try { EnabledChanged?.Invoke(on); }
            catch (Exception ex) { Swallowed(ex, "diagnostics: EnabledChanged listener"); }
        }
    }

    /// <summary>
    /// The window is closing. Writes the last summary and session.json, and waits a bounded
    /// moment for the writer to finish so the process does not end mid-line.
    /// </summary>
    public static void Shutdown()
    {
        StopTimers();

        if (_mode != 1)
        {
            return;
        }

        Enqueue(LogFile.Events, "session", "end", new { reason = "Aviary closed" });
        RunOnWriter(() => CloseOnWriter("Aviary closed"), 4000);
        _mode = 2;
    }

    /// <summary>Waits until everything queued so far is on disk, or the timeout passes.</summary>
    public static bool Flush(int timeoutMs = 2000) => RunOnWriter(FlushStreams, timeoutMs);

    // ══ ENQUEUEING ══════════════════════════════════════════════════════════

    private static void Enqueue(LogFile file, string kind, string? name, object? data)
    {
        if (_mode == 2)
        {
            return;
        }

        Entry e = new()
        {
            File = file,
            Time = DateTimeOffset.Now,
            Kind = kind,
            Name = name,
            Data = data,
            Session = _mode == 1 ? _session : null,
        };

        if (!Queue.Writer.TryWrite(e))
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    private static void EnqueueCommand(Action command)
    {
        Entry e = new() { Command = command, Time = DateTimeOffset.Now };

        if (!Queue.Writer.TryWrite(e))
        {
            // Full. A command must not be lost the way a record may be, so wait for room — on
            // the thread pool, never on the caller.
            _ = Queue.Writer.WriteAsync(e).AsTask();
        }
    }

    /// <summary>
    /// Runs <paramref name="command"/> on the writer after everything queued before it, and
    /// waits up to <paramref name="timeoutMs"/>. Runs inline if already on the writer.
    /// </summary>
    private static bool RunOnWriter(Action command, int timeoutMs)
    {
        if (_onWriter)
        {
            command();
            return true;
        }

        // Not disposed: if the wait times out the writer still calls Set() later, and a
        // disposed event would throw there instead.
        ManualResetEventSlim done = new(false);
        EnqueueCommand(() =>
        {
            try { command(); }
            finally { done.Set(); }
        });

        return done.Wait(timeoutMs);
    }

    // ══ THE WRITER ══════════════════════════════════════════════════════════

    /// <summary>Records that arrived before start-up decided on or off.</summary>
    private static readonly List<Entry> Held = new();

    /// <summary>Writer thread only: whether the category files are open for appending.</summary>
    private static bool _open;

    private static readonly FileStream?[] Streams = new FileStream?[FileNames.Length];
    private static readonly long[] Sizes = new long[FileNames.Length];

    private static void StartWriter()
    {
        if (Interlocked.Exchange(ref _writerStarted, 1) == 1)
        {
            return;
        }

        Thread thread = new(WriterMain)
        {
            IsBackground = true,
            Name = "Aviary diagnostics writer",
            Priority = ThreadPriority.BelowNormal,
        };
        thread.Start();
    }

    private static void WriterMain()
    {
        _onWriter = true;
        ChannelReader<Entry> reader = Queue.Reader;
        List<Entry> batch = new(512);

        while (true)
        {
            try
            {
                if (!reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
                {
                    return;
                }
            }
            catch (Exception)
            {
                return;
            }

            // Drained in batches and flushed once per batch — a busy page's requests become
            // one write, not hundreds.
            batch.Clear();
            while (batch.Count < 512 && reader.TryRead(out Entry? next))
            {
                batch.Add(next);
            }

            foreach (Entry e in batch)
            {
                try
                {
                    if (e.Command is not null)
                    {
                        FlushStreams();
                        e.Command();
                    }
                    else
                    {
                        Accept(e);
                    }
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref _writeErrors);
                }
            }

            try
            {
                FlushStreams();
            }
            catch (Exception)
            {
                Interlocked.Increment(ref _writeErrors);
            }
        }
    }

    private static void Accept(Entry e)
    {
        if (_open)
        {
            WriteEntry(e);
        }
        else if (_mode == 0 && Held.Count < 1000)
        {
            Held.Add(e);
        }

        // Otherwise collection is off, and dropping the record is the whole point.
    }

    /// <summary>The folder exists, is tidy, explains itself, and is ready to append to.</summary>
    private static void OpenOnWriter(Entry start)
    {
        Directory.CreateDirectory(Folder);
        Directory.CreateDirectory(CrashFolder);
        Directory.CreateDirectory(ArchiveFolder);

        // Before session.json is overwritten: did the LAST session end properly?
        NoteUncleanPreviousSession();

        Housekeep();
        WriteManifest();

        _open = true;
        WriteEntry(start);

        foreach (Entry held in Held)
        {
            WriteEntry(held);
        }

        Held.Clear();
        WriteSessionFile(ended: null);
        FlushStreams();
    }

    private static void CloseOnWriter(string reason)
    {
        if (!_open)
        {
            Held.Clear();
            return;
        }

        FlushStreams();

        try { BuildSummary(); }
        catch (Exception) { Interlocked.Increment(ref _writeErrors); }

        try { WriteSessionFile(ended: reason); }
        catch (Exception) { Interlocked.Increment(ref _writeErrors); }

        CloseStreams();
        _open = false;
        Held.Clear();
    }

    private static string PathOf(int file) => Path.Combine(Folder, FileNames[file]);

    private static void WriteEntry(Entry e)
    {
        byte[] line = SerialiseLine(e);
        int i = (int)e.File;

        try
        {
            FileStream stream = Streams[i] ??= OpenStream(i);
            stream.Write(line, 0, line.Length);
        }
        catch (IOException)
        {
            // Disk full, or the folder deleted from under us in Explorer. Drop the handle so
            // the next record reopens (and recreates) the file rather than writing into a
            // ghost.
            CloseStream(i);
            throw;
        }

        Sizes[i] += line.Length;
        Interlocked.Increment(ref _linesWritten);
        CountForSummary(e);

        if (Sizes[i] >= RotateBytes)
        {
            Rotate(i);
        }
    }

    private static FileStream OpenStream(int i)
    {
        Directory.CreateDirectory(Folder);

        // ReadWrite + Delete sharing, so the files can be read (and the folder deleted) while
        // Aviary is running.
        FileStream stream = new(PathOf(i), FileMode.Append, FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
        Sizes[i] = stream.Length;
        return stream;
    }

    private static void FlushStreams()
    {
        foreach (FileStream? stream in Streams)
        {
            stream?.Flush();
        }
    }

    private static void CloseStream(int i)
    {
        try { Streams[i]?.Dispose(); }
        catch (Exception) { Interlocked.Increment(ref _writeErrors); }

        Streams[i] = null;
    }

    private static void CloseStreams()
    {
        for (int i = 0; i < Streams.Length; i++)
        {
            CloseStream(i);
        }
    }

    // ══ SERIALISING — every string passes through Scrub ══════════════════════

    private static string Stamp(DateTimeOffset time) =>
        time.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);

    private static byte[] SerialiseLine(Entry e)
    {
        ArrayBufferWriter<byte> buffer = new(256);

        using (Utf8JsonWriter w = new(buffer, LineOptions))
        {
            w.WriteStartObject();
            w.WriteString("t", Stamp(e.Time));
            w.WriteString("s", e.Session ?? _session);
            w.WriteString("k", e.Kind);

            if (e.Name is not null)
            {
                w.WriteString("name", Scrub(e.Name));
            }

            if (e.Data is not null)
            {
                JsonElement data = JsonSerializer.SerializeToElement(e.Data, e.Data.GetType(), DataOptions);
                if (data.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty property in data.EnumerateObject())
                    {
                        if (property.NameEquals("t") || property.NameEquals("s") || property.NameEquals("k"))
                        {
                            continue;
                        }

                        w.WritePropertyName(property.Name);
                        WriteScrubbed(w, property.Value);
                    }
                }
            }

            w.WriteEndObject();
        }

        byte[] line = new byte[buffer.WrittenCount + 1];
        buffer.WrittenSpan.CopyTo(line);
        line[^1] = (byte)'\n';
        return line;
    }

    /// <summary>A whole JSON document — crash reports, session.json, manifest.json.</summary>
    private static byte[] SerialiseDocument(object data)
    {
        ArrayBufferWriter<byte> buffer = new(4096);
        using (Utf8JsonWriter w = new(buffer, DocumentOptions))
        {
            WriteScrubbed(w, JsonSerializer.SerializeToElement(data, data.GetType(), DataOptions));
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Copies a JSON value, replacing every string with its scrubbed form.
    ///
    /// This is the one door to the disk. Call sites do not have to remember to mask a URL,
    /// because nothing they hand over reaches a file without coming through here — a URL
    /// buried in an error message or a stack trace is caught exactly like one in a field
    /// called "url".
    /// </summary>
    private static void WriteScrubbed(Utf8JsonWriter w, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                w.WriteStartObject();
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    w.WritePropertyName(property.Name);
                    WriteScrubbed(w, property.Value);
                }

                w.WriteEndObject();
                break;

            case JsonValueKind.Array:
                w.WriteStartArray();
                foreach (JsonElement item in value.EnumerateArray())
                {
                    WriteScrubbed(w, item);
                }

                w.WriteEndArray();
                break;

            case JsonValueKind.String:
                w.WriteStringValue(Scrub(value.GetString()));
                break;

            default:
                value.WriteTo(w);
                break;
        }
    }

    /// <summary>Writes beside the target and moves into place, so a reader never sees half a file.</summary>
    private static void WriteFileAtomically(string path, byte[] bytes)
    {
        string? folder = Path.GetDirectoryName(path);
        if (folder is not null)
        {
            Directory.CreateDirectory(folder);
        }

        string temp = path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
    }

    // ══ ROTATION, THE CAP, RETENTION ════════════════════════════════════════

    private static void Rotate(int i)
    {
        CloseStream(i);

        string source = PathOf(i);
        if (!File.Exists(source))
        {
            return;
        }

        Directory.CreateDirectory(ArchiveFolder);
        string stem = Path.GetFileNameWithoutExtension(FileNames[i]);
        string target = Path.Combine(ArchiveFolder,
            stem + "." + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".jsonl");

        File.Move(source, target, overwrite: true);
        Sizes[i] = 0;
        EnforceCap();
    }

    /// <summary>
    /// 200 MB, oldest first. Archives go before anything else; the active files and the
    /// crash reports are the last thing anyone would want gone.
    /// </summary>
    private static void EnforceCap()
    {
        if (!Directory.Exists(Folder))
        {
            return;
        }

        List<FileInfo> all = new DirectoryInfo(Folder)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .ToList();

        long total = all.Sum(f => f.Length);
        if (total <= CapBytes)
        {
            return;
        }

        IEnumerable<FileInfo> victims = all
            .Where(f => f.DirectoryName is not null
                        && f.DirectoryName.Equals(ArchiveFolder, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.LastWriteTimeUtc)
            .Concat(all
                .Where(f => f.DirectoryName is not null
                            && f.DirectoryName.Equals(CrashFolder, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.LastWriteTimeUtc));

        foreach (FileInfo victim in victims)
        {
            if (total <= CapBytes)
            {
                break;
            }

            try
            {
                long length = victim.Length;
                victim.Delete();
                total -= length;
            }
            catch (Exception)
            {
                Interlocked.Increment(ref _writeErrors);
            }
        }

        // Still over with nothing left to archive: the live files themselves are too big.
        // Rotate the largest; the next pass deletes it.
        if (total > CapBytes)
        {
            int largest = Enumerable.Range(0, FileNames.Length).OrderByDescending(i => Sizes[i]).First();
            if (Sizes[largest] > 0)
            {
                Rotate(largest);
            }
        }
    }

    /// <summary>
    /// Thirty days. Archives and crash reports older than that are deleted; an active file
    /// whose first line is older is rewritten without its old lines.
    /// </summary>
    private static void Housekeep()
    {
        if (!Directory.Exists(Folder))
        {
            // Deleted by hand while collecting. Start again cleanly rather than writing into
            // handles that point at files which no longer exist.
            CloseStreams();
            Directory.CreateDirectory(Folder);
            if (_open)
            {
                WriteManifest();
            }
        }

        Directory.CreateDirectory(CrashFolder);
        Directory.CreateDirectory(ArchiveFolder);

        DateTime cutoff = DateTime.UtcNow.AddDays(-RetentionDays);

        foreach (string dir in new[] { ArchiveFolder, CrashFolder })
        {
            foreach (FileInfo old in new DirectoryInfo(dir).EnumerateFiles().Where(f => f.LastWriteTimeUtc < cutoff).ToList())
            {
                try { old.Delete(); }
                catch (Exception) { Interlocked.Increment(ref _writeErrors); }
            }
        }

        DateTimeOffset lineCutoff = DateTimeOffset.Now.AddDays(-RetentionDays);
        for (int i = 0; i < FileNames.Length; i++)
        {
            try { TrimActive(i, lineCutoff); }
            catch (Exception) { Interlocked.Increment(ref _writeErrors); }
        }

        EnforceCap();
    }

    private static void TrimActive(int i, DateTimeOffset cutoff)
    {
        string path = PathOf(i);
        if (!File.Exists(path))
        {
            return;
        }

        string? first;
        using (StreamReader reader = new(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)))
        {
            first = reader.ReadLine();
        }

        if (first is null || !TryLineTime(first, out DateTimeOffset firstTime) || firstTime >= cutoff)
        {
            return;
        }

        CloseStream(i);

        string temp = path + ".trim";
        using (StreamReader reader = new(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)))
        using (StreamWriter writer = new(temp, append: false, new UTF8Encoding(false)))
        {
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (!TryLineTime(line, out DateTimeOffset time) || time >= cutoff)
                {
                    writer.Write(line);
                    writer.Write('\n');
                }
            }
        }

        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Every line starts {"t":"…", so the time is read without parsing the JSON.</summary>
    private static bool TryLineTime(string line, out DateTimeOffset time)
    {
        time = default;
        const string prefix = "{\"t\":\"";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        int end = line.IndexOf('"', prefix.Length);
        return end > prefix.Length
            && DateTimeOffset.TryParse(line.AsSpan(prefix.Length, end - prefix.Length),
                CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }

    // ══ DELETE AND OPEN ═════════════════════════════════════════════════════

    /// <summary>True when the folder exists and holds anything.</summary>
    public static bool HasData
    {
        get
        {
            try
            {
                return Directory.Exists(Folder)
                    && Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories).Any();
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>(files, bytes) currently in the folder.</summary>
    public static (int Files, long Bytes) Stats()
    {
        try
        {
            if (!Directory.Exists(Folder))
            {
                return (0, 0);
            }

            List<FileInfo> files = new DirectoryInfo(Folder).EnumerateFiles("*", SearchOption.AllDirectories).ToList();
            return (files.Count, files.Sum(f => f.Length));
        }
        catch (Exception)
        {
            return (0, 0);
        }
    }

    /// <summary>
    /// Deletes everything collected. If collection is on it carries on into a fresh folder —
    /// "delete what you have" is not the same request as "stop".
    /// </summary>
    public static void DeleteAll()
    {
        RunOnWriter(() =>
        {
            CloseStreams();

            if (Directory.Exists(Folder))
            {
                try
                {
                    Directory.Delete(Folder, recursive: true);
                }
                catch (Exception)
                {
                    // Something (Explorer's preview pane, usually) holds a file. Take what can
                    // be taken rather than nothing.
                    foreach (string file in Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories).ToList())
                    {
                        try { File.Delete(file); }
                        catch (Exception) { Interlocked.Increment(ref _writeErrors); }
                    }
                }
            }

            ResetSessionCounters();

            if (_open)
            {
                Directory.CreateDirectory(CrashFolder);
                Directory.CreateDirectory(ArchiveFolder);
                WriteManifest();
                WriteSessionFile(ended: null);
            }
        }, 5000);

        Ui("improvement-data-deleted");
    }

    public static void OpenFolder()
    {
        try
        {
            if (!Directory.Exists(Folder))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "\"" + Folder + "\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Swallowed(ex, "diagnostics: open folder");
        }
    }

    // ══ THE CATEGORIES — what callers record ════════════════════════════════
    //
    // Numbers match the 16 categories in manifest.json, summary.md and the Settings list.

    /// <summary>1 and 2 — a navigation, a search, a title, a same-document route change.</summary>
    public static void Browse(string kind, string crumb, object data)
    {
        if (!RecordsBrowsing)
        {
            return;
        }

        Crumb(kind, crumb);
        Enqueue(LogFile.Browsing, kind, null, data);

        if (kind == "navigation")
        {
            Interlocked.Increment(ref _navigations);
        }
    }

    /// <summary>3 — one request, logged when its response arrives.</summary>
    public static void Request(NetRecord record)
    {
        if (!RecordsBrowsing)
        {
            return;
        }

        Enqueue(LogFile.Network, "request", null, record);
    }

    /// <summary>4 — one request the blocker stopped.</summary>
    public static void Blocked(int tab, string url, string rule, string context, string page)
    {
        if (!RecordsBrowsing)
        {
            return;
        }

        Interlocked.Increment(ref _blocked);
        RuleCounts.AddOrUpdate(rule, 1, (_, n) => n + 1);
        Enqueue(LogFile.Blocking, "blocked", null, new { tab, url, rule, context, page });
    }

    /// <summary>4 — a change to what the blocker does.</summary>
    public static void Shields(string what, object? data = null)
    {
        if (_mode != 1)
        {
            return;
        }

        Crumb("shields", what);
        Enqueue(LogFile.Blocking, "shields", what, data);
    }

    /// <summary>5 — something done in Aviary's own chrome.</summary>
    public static void Ui(string name, object? data = null)
    {
        if (_mode != 1)
        {
            return;
        }

        Crumb("ui", name);
        Enqueue(LogFile.Events, "ui", name, data);
    }

    /// <summary>6 — a setting changed. Called by AppSettings for every change.</summary>
    public static void Setting(string key, string? from, string to)
    {
        if (string.Equals(key, "privateMode", StringComparison.OrdinalIgnoreCase))
        {
            _private = string.Equals(to, "true", StringComparison.OrdinalIgnoreCase);
        }

        if (_mode != 1 || string.Equals(from, to, StringComparison.Ordinal))
        {
            return;
        }

        Crumb("setting", key + ": " + from + " → " + to);
        Enqueue(LogFile.Events, IsExperimentKey(key) ? "experiment" : "setting", key, new { from, to });
    }

    /// <summary>14 — a feature used, and Reader's verdict on a page.</summary>
    public static void Feature(string name, object? data = null)
    {
        if (_mode != 1)
        {
            return;
        }

        Crumb("feature", name);
        FeatureCounts.AddOrUpdate(name, 1, (_, n) => n + 1);
        Enqueue(LogFile.Events, "feature", name, data);
    }

    /// <summary>15 — a download starting or finishing.</summary>
    public static void Download(string stage, object data)
    {
        if (_mode != 1)
        {
            return;
        }

        Crumb("download", stage);
        Enqueue(LogFile.Events, "download", stage, data);
    }

    /// <summary>8 — a script error, rejected promise or failed resource on a page.</summary>
    public static void PageError(string kind, object data)
    {
        if (!RecordsBrowsing)
        {
            return;
        }

        Interlocked.Increment(ref _pageErrors);
        Enqueue(LogFile.Pages, kind, null, data);
    }

    /// <summary>9 — a WebView2 process failed: renderer, GPU, browser, utility.</summary>
    public static void Engine(object data, string crumb)
    {
        if (_mode != 1)
        {
            return;
        }

        Crumb("engine", crumb);
        Interlocked.Increment(ref _engineFailures);
        Enqueue(LogFile.Issues, "engine", null, data);
    }

    // Swallowed errors are counted per location so a catch that fires on every hover cannot
    // bury everything else. The first few in a session are written in full, with the stack;
    // the rest are counted and reported once a minute as "repeats".
    private const int SwallowedInFull = 3;
    private static readonly ConcurrentDictionary<string, int> SwallowedCounts = new();
    private static readonly ConcurrentDictionary<string, int> SwallowedSinceSample = new();

    /// <summary>
    /// 10 — a failure that was caught and survived. Every former <c>catch (Exception) {}</c>
    /// calls this. A near-free no-op while collection is off.
    /// </summary>
    public static void Swallowed(Exception ex, string where,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        if (_mode == 2 || ex is null)
        {
            return;
        }

        string at = Path.GetFileName(file) + ":" + line;
        string key = at + "|" + ex.GetType().Name;
        int count = SwallowedCounts.AddOrUpdate(key, 1, (_, n) => n + 1);

        if (count > SwallowedInFull)
        {
            SwallowedSinceSample.AddOrUpdate(key, 1, (_, n) => n + 1);
            return;
        }

        Crumb("swallowed", where);
        Enqueue(LogFile.Issues, "swallowed", where, new
        {
            at,
            occurrence = count,
            exception = DescribeException(ex),
        });
    }

    private static readonly ConcurrentDictionary<string, int> AnomalyCounts = new();
    private const int AnomaliesInFull = 40;

    /// <summary>
    /// 11 — something Aviary noticed was wrong about itself: a setting that did not come back
    /// the way it was saved, an icon that would not load, a tab that did not start, a stall.
    /// </summary>
    public static void Anomaly(string kind, string detail, object? data = null)
    {
        if (_mode == 2)
        {
            return;
        }

        int count = AnomalyCounts.AddOrUpdate(kind, 1, (_, n) => n + 1);
        if (count > AnomaliesInFull)
        {
            return;
        }

        Crumb("anomaly", kind + ": " + detail);
        Enqueue(LogFile.Issues, "anomaly", kind, new { detail, data });
    }

    /// <summary>Any other issue line — an unobserved task, an unclean previous exit.</summary>
    private static void Issue(string kind, string? name, object data)
    {
        if (_mode == 2)
        {
            return;
        }

        Crumb("issue", kind);
        Enqueue(LogFile.Issues, kind, name, data);
    }

    /// <summary>13 — a timing or resource measurement.</summary>
    public static void Perf(string kind, object data)
    {
        if (_mode != 1)
        {
            return;
        }

        Enqueue(LogFile.Perf, kind, null, data);
    }

    // ══ 12 — CRASHES ════════════════════════════════════════════════════════

    /// <summary>
    /// Written synchronously, straight to crashes/, NOT through the queue: a terminating crash
    /// ends the process when this returns, and a queued record would die with it.
    /// </summary>
    public static void Crash(Exception ex, string source, bool terminating)
    {
        if (_mode != 1 || ex is null)
        {
            return;
        }

        try
        {
            DateTimeOffset now = DateTimeOffset.Now;
            string name = now.ToString("yyyy-MM-dd'T'HH-mm-ss-fff", CultureInfo.InvariantCulture) + ".json";

            var report = new
            {
                t = Stamp(now),
                session = _session,
                source,
                terminating,
                build = new { version = BuildInfo.Version, channel = BuildInfo.Channel, phase = BuildInfo.PhaseLabel },
                uptimeSeconds = Math.Round((now - ProcessStart).TotalSeconds, 1),
                exception = DescribeException(ex),
                lastActions = CrumbsSnapshot(),
                openTabs = TabsSnapshot(),
                experimentsOn = ActiveExperiments(),
                memoryMB = Math.Round(Environment.WorkingSet / 1048576.0, 1),
            };

            WriteFileAtomically(Path.Combine(CrashFolder, name), SerialiseDocument(report));
            Interlocked.Increment(ref _crashes);

            Issue("crash", null, new
            {
                file = "crashes/" + name,
                source,
                terminating,
                type = ex.GetType().FullName,
                message = ex.Message,
            });

            if (terminating)
            {
                Flush(1500);
            }
        }
        catch (Exception)
        {
            // A crash while recording a crash has nowhere left to be recorded.
        }
    }

    private static object DescribeException(Exception? ex, int depth = 0)
    {
        if (ex is null)
        {
            return new { };
        }

        return new
        {
            type = ex.GetType().FullName,
            message = ex.Message,
            hresult = "0x" + ex.HResult.ToString("X8", CultureInfo.InvariantCulture),
            stack = ex.StackTrace,
            inner = ex.InnerException is not null && depth < 3
                ? DescribeException(ex.InnerException, depth + 1)
                : null,
        };
    }

    // ══ BREADCRUMBS — the last hundred things, for a crash report ════════════

    private sealed record Crumbed(DateTimeOffset T, string K, string What);

    private static readonly Queue<Crumbed> Crumbs = new();

    private static void Crumb(string kind, string what)
    {
        if (what.Length > 240)
        {
            what = what.Substring(0, 240) + "…";
        }

        lock (Crumbs)
        {
            Crumbs.Enqueue(new Crumbed(DateTimeOffset.Now, kind, what));
            while (Crumbs.Count > 100)
            {
                Crumbs.Dequeue();
            }
        }
    }

    private static List<object> CrumbsSnapshot()
    {
        lock (Crumbs)
        {
            return Crumbs.Select(c => (object)new { t = Stamp(c.T), k = c.K, what = c.What }).ToList();
        }
    }

    private static string LastCrumb()
    {
        lock (Crumbs)
        {
            return Crumbs.Count == 0 ? string.Empty : Crumbs.Last().K + " " + Crumbs.Last().What;
        }
    }

    // ══ OPEN TABS — kept always, so switching on mid-session knows what is open ═══

    private sealed class TabInfo
    {
        public string Url = string.Empty;
        public string Title = string.Empty;
        public bool Pinned;
    }

    private static readonly ConcurrentDictionary<int, TabInfo> Tabs = new();
    private static int _nextTabId;
    private static volatile int _activeTab;

    public static int NewTabId()
    {
        int id = Interlocked.Increment(ref _nextTabId);
        Tabs[id] = new TabInfo();
        return id;
    }

    public static void TabUrl(int id, string url)
    {
        if (Tabs.TryGetValue(id, out TabInfo? tab))
        {
            tab.Url = url;
        }
    }

    public static void TabTitle(int id, string title)
    {
        if (Tabs.TryGetValue(id, out TabInfo? tab))
        {
            tab.Title = title;
        }
    }

    public static void TabPinned(int id, bool pinned)
    {
        if (Tabs.TryGetValue(id, out TabInfo? tab))
        {
            tab.Pinned = pinned;
        }
    }

    public static void TabActive(int id) => _activeTab = id;

    public static void TabClosed(int id) => Tabs.TryRemove(id, out _);

    private static List<object> TabsSnapshot() =>
        Tabs.OrderBy(p => p.Key)
            .Select(p => (object)new
            {
                id = p.Key,
                url = p.Value.Url,
                title = p.Value.Title,
                pinned = p.Value.Pinned,
                active = p.Key == _activeTab,
            })
            .ToList();

    // ══ 7 — EXPERIMENTS ═════════════════════════════════════════════════════

    /// <summary>
    /// The Experimental menu (M6) keys its flags "exp.*". Nothing uses the prefix yet; when
    /// it does, flag changes are filed as experiments and crash reports list the flags on.
    /// </summary>
    private static bool IsExperimentKey(string key) =>
        key.StartsWith("exp.", StringComparison.OrdinalIgnoreCase);

    private static List<string> ActiveExperiments() =>
        AppSettings.Snapshot()
            .Where(p => IsExperimentKey(p.Key)
                        && p.Value.Length > 0
                        && !p.Value.Equals("false", StringComparison.OrdinalIgnoreCase)
                        && !p.Value.Equals("off", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Key + "=" + p.Value)
            .ToList();

    // ══ STARTUP PHASES ══════════════════════════════════════════════════════

    private static readonly List<(string Phase, double Ms)> Marks = new();
    private static int _startupWritten;

    /// <summary>
    /// A start-up milestone, in milliseconds since the process started. Recorded whether or
    /// not collection is on — it is a handful of numbers — and written once, when the first
    /// page has finished loading.
    /// </summary>
    public static void Mark(string phase)
    {
        double ms = Math.Round((DateTimeOffset.Now - ProcessStart).TotalMilliseconds, 1);

        lock (Marks)
        {
            if (Marks.Any(m => m.Phase == phase))
            {
                return;
            }

            Marks.Add((phase, ms));
        }

        if (phase == "first-page-loaded" && Interlocked.Exchange(ref _startupWritten, 1) == 0)
        {
            Dictionary<string, double> phases;
            lock (Marks)
            {
                phases = Marks.ToDictionary(m => m.Phase, m => m.Ms);
            }

            TrackSlow("startup", ms, "process start to first page");
            Perf("startup", new { totalMs = ms, phases });
        }
    }

    private static DateTimeOffset ReadProcessStart()
    {
        try
        {
            using Process self = Process.GetCurrentProcess();
            return new DateTimeOffset(self.StartTime);
        }
        catch (Exception)
        {
            return DateTimeOffset.Now;
        }
    }

    // ══ 13 — THE BLOCKER'S COST, sampled once a minute ═══════════════════════

    private static long _filterCount;
    private static long _filterTicks;
    private static long _filterMaxTicks;

    /// <summary>How long one blocker match took. Aggregated, never logged per request.</summary>
    public static void FilterCost(long ticks)
    {
        if (_mode != 1)
        {
            return;
        }

        Interlocked.Increment(ref _filterCount);
        Interlocked.Add(ref _filterTicks, ticks);

        long max = Interlocked.Read(ref _filterMaxTicks);
        while (ticks > max)
        {
            long seen = Interlocked.CompareExchange(ref _filterMaxTicks, ticks, max);
            if (seen == max)
            {
                break;
            }

            max = seen;
        }
    }
}

/// <summary>
/// One network request, as handed to the writer. The first/third-party test and all masking
/// happen on the writer thread when this is serialised, so the UI thread pays for nothing but
/// the allocation.
/// </summary>
internal sealed class NetRecord
{
    public int Tab { get; init; }
    public string Url { get; init; } = string.Empty;
    public string Method { get; init; } = string.Empty;
    public string Context { get; init; } = string.Empty;
    public int Status { get; init; }

    /// <summary>The blocker rule that answered this request itself — it never left the machine.</summary>
    public string? BlockedBy { get; init; }

    public string? Type { get; init; }
    public long? Bytes { get; init; }
    public double? Ms { get; init; }
    public string? Initiator { get; init; }

    [JsonIgnore]
    public string PageUrl { get; init; } = string.Empty;

    /// <summary>The page's site — its registrable domain, not its whole address.</summary>
    public string Page => SiteOf(PageUrl);

    public string Party
    {
        get
        {
            string mine = SiteOf(Url);
            string page = Page;
            return page.Length == 0 || mine.Length == 0 ? "unknown"
                 : string.Equals(mine, page, StringComparison.OrdinalIgnoreCase) ? "first"
                 : "third";
        }
    }

    internal static string SiteOf(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Host.Length == 0)
        {
            return string.Empty;
        }

        return PublicSuffix.RegistrableDomain(uri.Host);
    }
}
