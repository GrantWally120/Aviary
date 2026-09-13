# Is Grant using the machine right now?
#
# GetLastInputInfo counts MY injected input too, so "idle" alone cannot tell us apart. Every
# script of mine that sends input stamps lastinput.txt with the tick count right after it
# finishes; input newer than that stamp (by more than a small margin) was not mine.
#
# Exit 0 = clear to drive the GUI. Exit 3 = someone else is active; do NOT send input.
param([int]$QuietMs = 15000)

Add-Type @'
using System; using System.Runtime.InteropServices;
public class GuardIdle {
  [StructLayout(LayoutKind.Sequential)] public struct LII { public uint cbSize; public uint dwTime; }
  [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LII p);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
  public static uint LastInputTick() { var l = new LII(); l.cbSize = (uint)Marshal.SizeOf(l); GetLastInputInfo(ref l); return l.dwTime; }
}
'@ -ErrorAction SilentlyContinue

$stampFile = Join-Path $PSScriptRoot 'lastinput.txt'
$mine = 0
if (Test-Path $stampFile) { $mine = [uint32](Get-Content $stampFile) }

$now = [uint32]([Environment]::TickCount -band 0xFFFFFFFF)
$last = [GuardIdle]::LastInputTick()
$idle = $now - $last
$sb = New-Object System.Text.StringBuilder 256
[void][GuardIdle]::GetWindowText([GuardIdle]::GetForegroundWindow(), $sb, 256)

$foreign = ($last -gt ($mine + 1500)) -and ($idle -lt $QuietMs)
Write-Output ("idle={0}ms  foreground='{1}'  lastInputIsMine={2}" -f $idle, $sb.ToString(), (-not ($last -gt ($mine + 1500))))
if ($foreign) { Write-Output "USER ACTIVE - not driving the GUI"; exit 3 }
exit 0
