# Listen to OutputDebugString — the DBWIN channel DebugView reads — without installing anything.
# WinUI and the Windows App SDK report their internal failures there ("ReturnHr", "FailFast",
# HRESULTs, module!address), which is what bug 50's investigation needed without a debugger.
# Only lines from processes whose name matches -Name are kept. Stops after -Seconds.
param([string]$Out = "$PSScriptRoot\shots\dbwin.txt", [int]$Seconds = 60, [string]$Name = "Aviary")

Add-Type @'
using System; using System.Collections.Generic; using System.Diagnostics; using System.IO;
using System.Runtime.InteropServices; using System.Text;
public static class DbWin {
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr CreateEventW(IntPtr sa, bool manual, bool initial, string name);
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr CreateFileMappingW(IntPtr file, IntPtr sa, uint protect, uint high, uint low, string name);
  [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr MapViewOfFile(IntPtr map, uint access, uint high, uint low, UIntPtr bytes);
  [DllImport("kernel32.dll")] static extern bool SetEvent(IntPtr h);
  [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
  public static string Listen(string outFile, int seconds, string name) {
    IntPtr ready = CreateEventW(IntPtr.Zero, false, false, "DBWIN_BUFFER_READY");
    if (Marshal.GetLastWin32Error() == 183) return "another debug-output listener is running (DebugView?) - not listening";
    IntPtr data = CreateEventW(IntPtr.Zero, false, false, "DBWIN_DATA_READY");
    IntPtr map = CreateFileMappingW(new IntPtr(-1), IntPtr.Zero, 4, 0, 4096, "DBWIN_BUFFER");
    IntPtr view = MapViewOfFile(map, 4, 0, 0, new UIntPtr(4096));
    if (ready == IntPtr.Zero || data == IntPtr.Zero || view == IntPtr.Zero) return "could not open the DBWIN channel: " + Marshal.GetLastWin32Error();
    var names = new Dictionary<int, string>();
    int kept = 0, all = 0;
    DateTime stop = DateTime.Now.AddSeconds(seconds);
    using (var w = new StreamWriter(outFile, false, new UTF8Encoding(false))) {
      w.AutoFlush = true;
      SetEvent(ready);
      while (DateTime.Now < stop) {
        if (WaitForSingleObject(data, 250) != 0) continue;
        int pid = Marshal.ReadInt32(view);
        string text = Marshal.PtrToStringAnsi(view + 4) ?? "";
        SetEvent(ready);
        all++;
        string pn;
        if (!names.TryGetValue(pid, out pn)) { try { pn = Process.GetProcessById(pid).ProcessName; } catch { pn = "?"; } names[pid] = pn; }
        if (pn.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;
        kept++;
        w.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " [" + pid + "] " + text.TrimEnd());
      }
    }
    return "listened " + seconds + " s: " + all + " lines from all processes, " + kept + " from " + name;
  }
}
'@ -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
[DbWin]::Listen($Out, $Seconds, $Name)
