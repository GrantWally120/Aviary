using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// One file being downloaded, wrapped so the chrome does not have to hold a COM object and
/// can survive the operation being torn down underneath it.
/// </summary>
internal sealed class DownloadItem
{
    private readonly CoreWebView2DownloadOperation _operation;

    public DownloadItem(CoreWebView2DownloadOperation operation)
    {
        _operation = operation;
        FilePath = operation.ResultFilePath ?? string.Empty;

        operation.StateChanged += (_, _) => Changed?.Invoke(this);
        operation.BytesReceivedChanged += (_, _) => Changed?.Invoke(this);
    }

    public event Action<DownloadItem>? Changed;

    public string FilePath { get; }

    public string FileName => string.IsNullOrEmpty(FilePath) ? "Download" : Path.GetFileName(FilePath);

    public bool IsComplete
    {
        get
        {
            try { return _operation.State == CoreWebView2DownloadState.Completed; }
            catch (Exception) { return false; }
        }
    }

    public bool IsInterrupted
    {
        get
        {
            try { return _operation.State == CoreWebView2DownloadState.Interrupted; }
            catch (Exception) { return false; }
        }
    }

    /// <summary>A short line for the menu: progress while running, size when finished.</summary>
    public string Status
    {
        get
        {
            try
            {
                if (IsInterrupted)
                {
                    return "Stopped";
                }

                long received = _operation.BytesReceived;

                if (IsComplete)
                {
                    return Describe(received);
                }

                long? total = _operation.TotalBytesToReceive;
                if (total is > 0)
                {
                    int percent = (int)Math.Round(received * 100.0 / total.Value);
                    return percent + "%  ·  " + Describe(received) + " of " + Describe(total.Value);
                }

                return Describe(received) + " so far";
            }
            catch (Exception)
            {
                return "Download";
            }
        }
    }

    public void Cancel()
    {
        try { _operation.Cancel(); }
        catch (Exception) { }
    }

    private static string Describe(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        int unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return (unit == 0 ? value.ToString("0") : value.ToString("0.#")) + " " + units[unit];
    }
}

/// <summary>
/// Everything downloaded this session, newest first. Not persisted yet — a real download
/// history belongs with the rest of the history work in Phase 4.
/// </summary>
internal static class Downloads
{
    public static List<DownloadItem> Items { get; } = new();

    public static event Action? Changed;

    public static void Add(DownloadItem item)
    {
        Items.Insert(0, item);
        item.Changed += _ => Changed?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>Shows the file in Explorer, selected — what every browser does.</summary>
    public static void Reveal(DownloadItem item)
    {
        try
        {
            if (!File.Exists(item.FilePath))
            {
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "/select,\"" + item.FilePath + "\"",
                UseShellExecute = true
            });
        }
        catch (Exception)
        {
            // Explorer refusing to open is not worth taking the browser down for.
        }
    }
}
