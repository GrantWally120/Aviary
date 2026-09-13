# Print the test instance's window rectangle (no input, no focus change).
$sp = $PSScriptRoot
Add-Type @'
using System; using System.Runtime.InteropServices;
public class WinRect { [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RC r);
  public struct RC { public int L, T, R, B; } }
'@ -ErrorAction SilentlyContinue
$p = Get-Process -Id ([int](Get-Content (Join-Path $sp 'pid.txt')))
$r = New-Object WinRect+RC
[void][WinRect]::GetWindowRect($p.MainWindowHandle, [ref]$r)
"rect {0},{1} {2}x{3}  title='{4}'" -f $r.L, $r.T, ($r.R - $r.L), ($r.B - $r.T), $p.MainWindowTitle
