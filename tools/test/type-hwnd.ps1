# Type into ONE specific window (e.g. Aviary's private window), fronting it first and refusing
# to send anything unless that exact window owns the foreground.
param([long]$Hwnd, [string]$Text, [int]$WaitMs = 1500)
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public class TH {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool f);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
}
"@ -ErrorAction SilentlyContinue
$h = [IntPtr]$Hwnd
if ([TH]::GetForegroundWindow() -ne $h) {
  $fg = [TH]::GetForegroundWindow(); $them = [TH]::GetWindowThreadProcessId($fg, [IntPtr]::Zero); $me = [TH]::GetCurrentThreadId()
  [void][TH]::AttachThreadInput($them, $me, $true); [void][TH]::BringWindowToTop($h); [void][TH]::SetForegroundWindow($h); [void][TH]::AttachThreadInput($them, $me, $false)
  Start-Sleep -Milliseconds 700
}
if ([TH]::GetForegroundWindow() -ne $h) { Write-Output "ABORT: window $Hwnd is not foreground - nothing sent"; exit 2 }
[System.Windows.Forms.SendKeys]::SendWait($Text)
Set-Content -Path (Join-Path $PSScriptRoot "lastinput.txt") -Value ([uint32]([Environment]::TickCount -band 0xFFFFFFFF))
Start-Sleep -Milliseconds $WaitMs
Write-Output "sent to $Hwnd"
