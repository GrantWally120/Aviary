# What Windows thinks is under a point of a test Aviary's window - WM_NCHITTEST, no input sent.
# (M4.5, the Liquid Glass caption buttons.) Points are named toolbar controls, found through UI
# Automation, so the probe follows the layout at any size or scale.
#   hit-probe.ps1 -ProcessId <pid> [-Names "Minimise,Maximise,Close,Home,Menu"] [-Extra "x,y;x,y"]
param([int]$ProcessId, [string]$Names = "Minimise,Maximise,Restore,Close", [string]$Extra = "")

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class HitProbe {
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
}
'@
$A = [System.Windows.Automation.AutomationElement]; $T = [System.Windows.Automation.TreeScope]
$root = $A::RootElement.FindFirst($T::Children, (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $ProcessId)))
if (-not $root) { "no window for $ProcessId"; exit 1 }
$hwnd = [IntPtr]$root.Current.NativeWindowHandle
$codes = @{ 0='HTNOWHERE'; 1='HTCLIENT'; 2='HTCAPTION'; 3='HTSYSMENU'; 8='HTMINBUTTON'; 9='HTMAXBUTTON'; 12='HTTOP'; 14='HTTOPRIGHT'; 20='HTCLOSE'; -1='HTTRANSPARENT' }

function Probe($label, $x, $y) {
    $lp = [IntPtr](($y -shl 16) -bor ($x -band 0xFFFF))
    $h = [int][HitProbe]::SendMessage($hwnd, 0x84, [IntPtr]::Zero, $lp)
    $name = if ($codes.ContainsKey($h)) { $codes[$h] } else { "$h" }
    "{0,-10} {1,5},{2,-5} -> {3}" -f $label, $x, $y, $name
}

foreach ($n in $Names.Split(',')) {
    $el = $root.FindFirst($T::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $n.Trim())))
    if (-not $el) { continue }
    $r = $el.Current.BoundingRectangle
    Probe $n.Trim() ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2))
}
if ($Extra) { foreach ($p in $Extra.Split(';')) { $xy = $p.Split(','); Probe "point" ([int]$xy[0]) ([int]$xy[1]) } }
