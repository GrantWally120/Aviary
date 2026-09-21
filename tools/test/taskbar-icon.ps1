# What does the TASKBAR actually show for this window? (M4.8, bug 77.)
#
# Aviary draws its own title bar (ExtendsContentIntoTitleBar), so the window icon is never drawn
# inside Aviary itself - the taskbar and Alt-Tab are the ONLY places Settings -> App Icon can show.
# This finds the process's own taskbar button through UI Automation and captures just that button,
# so the claim "the taskbar follows the setting" can be measured instead of assumed.
# Reads a ~40x40 patch of the taskbar and nothing else. No input, no focus change.
#
#   taskbar-icon.ps1 -ProcessId <pid> -Out shots\taskbar.png
param([int]$ProcessId, [string]$Out = "shots\taskbar.png")

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, WindowsBase

$A = [System.Windows.Automation.AutomationElement]
$T = [System.Windows.Automation.TreeScope]

# The taskbar is its own top-level window; its buttons name themselves after the window.
$tray = $A::RootElement.FindFirst($T::Children,
    (New-Object System.Windows.Automation.PropertyCondition($A::ClassNameProperty, "Shell_TrayWnd")))
if (-not $tray) { "no taskbar found"; exit 1 }

$title = (Get-Process -Id $ProcessId).MainWindowTitle
$btn = $null
foreach ($el in $tray.FindAll($T::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($el.Current.ControlType.ProgrammaticName -eq 'ControlType.Button' -and $el.Current.Name -like "*Aviary*") { $btn = $el; break }
}
if (-not $btn) { "no taskbar button naming Aviary (window title: '$title')"; exit 2 }

$r = $btn.Current.BoundingRectangle
"taskbar button '$($btn.Current.Name)' at $([int]$r.X),$([int]$r.Y) $([int]$r.Width)x$([int]$r.Height)"

$bmp = New-Object System.Drawing.Bitmap ([int]$r.Width), ([int]$r.Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
$g.Dispose()
$bmp.Save((Join-Path $PWD $Out), [System.Drawing.Imaging.ImageFormat]::Png)

# Average the middle of the button - the glyph - ignoring the dark taskbar around it.
$r2 = 0; $g2 = 0; $b2 = 0; $n = 0
$x0 = [int]($bmp.Width * 0.28); $x1 = [int]($bmp.Width * 0.72)
$y0 = [int]($bmp.Height * 0.20); $y1 = [int]($bmp.Height * 0.72)
for ($x = $x0; $x -lt $x1; $x++) { for ($y = $y0; $y -lt $y1; $y++) {
    $c = $bmp.GetPixel($x, $y); $r2 += $c.R; $g2 += $c.G; $b2 += $c.B; $n++ } }
"taskbar glyph avg #{0:X2}{1:X2}{2:X2}  -> {3}" -f [int]($r2/$n), [int]($g2/$n), [int]($b2/$n), $Out
$bmp.Dispose()
