# Holds the left mouse button down on one control of a test Aviary, captures the screen around it
# while held, then releases — to see a pressed state (M4-era glass work, 14 Sep 2026: the toolbar
# buttons' 94% dip). Guarded like every desktop-input script here: it refuses unless Grant has been
# idle and the test window is in front, and it puts the cursor back where it was.
#   press-hold.ps1 -ProcessId <pid> -Id HomeButton -Out shots/press.png [-HoldMs 250]
param([int]$ProcessId, [string]$Id, [string]$Out, [int]$HoldMs = 250)

& "$PSScriptRoot\guard.ps1"; if ($LASTEXITCODE -ne 0) { exit 3 }

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class PH {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
}
"@

$fg = [PH]::GetForegroundWindow(); $fgPid = 0; [void][PH]::GetWindowThreadProcessId($fg, [ref]$fgPid)
if ($fgPid -ne $ProcessId) { "REFUSED: the test window is not in front"; exit 4 }

$A = [System.Windows.Automation.AutomationElement]
$root = $A::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,
    (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $ProcessId)))
$el = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $Id)))
if (-not $el) { "NOT FOUND $Id"; exit 1 }
$r = $el.Current.BoundingRectangle
$cx = [int]($r.X + $r.Width / 2); $cy = [int]($r.Y + $r.Height / 2)

$was = New-Object PH+POINT; [void][PH]::GetCursorPos([ref]$was)
# Also reports the width of the button's filled square in the middle row (hover or press brush),
# which is how a 94% dip shows up as numbers rather than as a squint at a zoomed picture.
function Shot([string]$path) {
  $bmp = New-Object System.Drawing.Bitmap 120, 80
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($cx - 60, $cy - 40, 0, 0, $bmp.Size)
  $bg = $bmp.GetPixel(2, 40); $l = -1; $rr = -1
  for ($x = 0; $x -lt 120; $x++) { $p = $bmp.GetPixel($x, 40); if ([math]::Abs([int]$p.R - $bg.R) + [math]::Abs([int]$p.G - $bg.G) + [math]::Abs([int]$p.B - $bg.B) -gt 6) { if ($l -lt 0) { $l = $x }; $rr = $x } }
  "  $([IO.Path]::GetFileName($path)): filled square spans x=$l..$rr ($($rr - $l + 1) px)"
  $big = New-Object System.Drawing.Bitmap 480, 320
  $g2 = [System.Drawing.Graphics]::FromImage($big); $g2.InterpolationMode = 'NearestNeighbor'
  $g2.DrawImage($bmp, 0, 0, 480, 320); $big.Save($path); $g.Dispose(); $g2.Dispose(); $bmp.Dispose(); $big.Dispose()
}

# A real move: SetCursorPos does not drive WinUI's pointer pipeline (HANDOFF §5), so hover never
# shows. mouse_event MOVE|ABSOLUTE with 0–65535 coordinates over the primary screen does.
$sw = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
[PH]::mouse_event(0x8001, [int]($cx * 65535 / ($sw.Width - 1)), [int]($cy * 65535 / ($sw.Height - 1)), 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 400
Shot ($Out -replace '\.png$', '-rest.png')
[PH]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)   # left down
Start-Sleep -Milliseconds $HoldMs
Shot $Out
[PH]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)   # left up
Start-Sleep -Milliseconds 120
[void][PH]::SetCursorPos($was.X, $was.Y)
"PRESSED $Id at $cx,$cy for $HoldMs ms -> $Out"
