# Start Page backgrounds: do all six choices work, and does a missing picture fail safely? (M4.9.)
#
# Five choices are drawn by the page out of the theme's own tokens, so they can be checked with no
# desktop input at all. The sixth opens a real Windows file picker, which cannot be: -Picker runs
# that half too, and it needs the machine to itself (guard.ps1 decides).
#
# Two things worth knowing, both learned the hard way here:
#  * On Windows 11 the picker is hosted by a SEPARATE process (PickerHost), so it is not among
#    Aviary's own windows and uia.ps1 -ProcessId cannot see it. It is found by class #32770.
#  * SetDlgItemText and WM_COMMAND/IDOK do nothing to a modern IFileDialog. The file name box has
#    to be clicked, typed into and Entered, like a person.
#  * A CDP screenshot paints position:fixed layers at the WRONG size when device metrics are
#    emulated, so the picture looks like it stops short. It does not - front-and-shoot.ps1 shows
#    it edge to edge. Never judge a fixed background from a cdp.js shot.
#
#   startbg-test.ps1 [-Profile sbg] [-Port 9355] [-Picker]
param([string]$Profile = "sbg", [int]$Port = 9355, [switch]$Picker)

$kit = $PSScriptRoot
Set-Location $kit
$env:CDP_PORT = "$Port"
$pass = 0; $fail = 0
function Check($what, $ok, $saw) {
    if ($ok) { $script:pass++; "  PASS  $what" }
    else { $script:fail++; "  FAIL  $what  --  saw: $saw" }
}
function Page { node cdp.js eval "#0" "JSON.stringify({bg:document.body.dataset.bg,sky:document.getElementById('sky').innerHTML.length,pic:document.getElementById('pic').dataset.on||'0'})" | ConvertFrom-Json | ConvertFrom-Json }
function Saved { (Get-Content "profiles\$Profile\settings.json" -Raw | ConvertFrom-Json).startBackground }
function Choose($name) {
    node cdp.js eval "#0" "(function(){var r=[].slice.call(document.querySelectorAll('.cust__row')).filter(function(e){return e.getAttribute('data-bg')==='$name';})[0]; if(!r) return 'NO ROW'; document.getElementById('custBtn').click(); r.click(); return 'ok';})()" | Out-Null
    Start-Sleep -Seconds 2
}

# A real picture, made here rather than shipped: 600x400, a sun on a blue field.
Add-Type -AssemblyName System.Drawing
$picture = Join-Path $kit "testpicture.png"
if (-not (Test-Path $picture)) {
    $bmp = New-Object System.Drawing.Bitmap 600, 400
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(20, 80, 140))
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(250, 200, 80))), 380, 40, 140, 140)
    $g.Dispose(); $bmp.Save($picture, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}

& .\launch-quiet.ps1 -Profile $Profile -Fresh -Port $Port `
    -SettingsJson '{"improvementCollection":"true","startNews":"false","listsAutoUpdate":"Off"}' | Out-Null
$pidv = [int](Get-Content pid.txt)
"test instance $pidv"
Start-Sleep -Seconds 4

"`n1. the dome is still what you get by default"
$p = Page
Check "the page says glasshouse" ($p.bg -eq 'glasshouse') $p.bg
Check "and really draws it" ($p.sky -gt 1000) $p.sky

"`n2. every drawn choice, from the Customise popover"
foreach ($name in @('Plain', 'Dawn', 'Dusk', 'Moss')) {
    Choose $name
    $p = Page
    Check "$name draws" ($p.bg -eq $name.ToLower() -and $p.sky -eq 0) ($p.bg + " sky=" + $p.sky)
    Check "$name is saved" ((Saved) -eq $name) (Saved)
}
Choose 'Glasshouse'
$p = Page
Check "and the dome comes back" ($p.bg -eq 'glasshouse' -and $p.sky -gt 1000) ($p.bg + " sky=" + $p.sky)

if ($Picker) {
    "`n3. Your picture - the real Windows file picker"
    & .\guard.ps1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        "  SKIPPED - someone is using the machine, and this one needs the keyboard"
    } else {
        Add-Type -AssemblyName System.Windows.Forms
        Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices;
public class FileDlg {
  [StructLayout(LayoutKind.Sequential)] public struct RC { public int L,T,R,B; }
  delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EP f, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RC r);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool f);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
  // The picker lives in PickerHost, not in Aviary: found by class, not by process.
  public static IntPtr Find() {
    IntPtr found = IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      if (!IsWindowVisible(h)) return true;
      var c = new StringBuilder(200); GetClassName(h, c, 200);
      var t = new StringBuilder(300); GetWindowText(h, t, 300);
      if (c.ToString() == "#32770" && t.ToString() == "Open") { found = h; return false; }
      return true;
    }, IntPtr.Zero);
    return found;
  }
  public static void Click(int x, int y) { SetCursorPos(x, y); mouse_event(0x0002,0,0,0,IntPtr.Zero); mouse_event(0x0004,0,0,0,IntPtr.Zero); }
  public static bool Front(IntPtr h) {
    IntPtr fg = GetForegroundWindow();
    uint them = GetWindowThreadProcessId(fg, IntPtr.Zero), me = GetCurrentThreadId();
    AttachThreadInput(them, me, true); BringWindowToTop(h); SetForegroundWindow(h); AttachThreadInput(them, me, false);
    System.Threading.Thread.Sleep(700);
    return GetForegroundWindow() == h;
  }
}
'@ -ErrorAction SilentlyContinue

        Choose 'Your picture'
        Start-Sleep -Seconds 3
        $dlg = [FileDlg]::Find()
        Check "the picker opened" ($dlg -ne [IntPtr]::Zero) $dlg

        if ($dlg -ne [IntPtr]::Zero) {
            Check "and it is in front" ([FileDlg]::Front($dlg)) ([FileDlg]::GetForegroundWindow())
            $r = New-Object FileDlg+RC
            [void][FileDlg]::GetWindowRect($dlg, [ref]$r)
            # The file name box, roughly a third up from the bottom of the dialog.
            [FileDlg]::Click(($r.L + [int](($r.R - $r.L) * 0.48)), ($r.B - 68))
            Start-Sleep -Milliseconds 500
            [System.Windows.Forms.SendKeys]::SendWait($picture)
            Start-Sleep -Milliseconds 600
            [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
            Set-Content -Path (Join-Path $kit "lastinput.txt") -Value ([uint32]([Environment]::TickCount -band 0xFFFFFFFF))
            Start-Sleep -Seconds 4

            Check "the picker closed" ([FileDlg]::Find() -eq [IntPtr]::Zero) ([FileDlg]::Find())
            $copy = Get-Item "profiles\$Profile\startbackground.img" -ErrorAction SilentlyContinue
            Check "the picture was copied into the profile" ($copy -and $copy.Length -eq (Get-Item $picture).Length) $copy.Length
            Check "the setting followed" ((Saved) -eq 'Your picture') (Saved)
            $p = Page
            Check "the page shows it" ($p.bg -eq 'picture' -and $p.pic -eq '1') ($p.bg + " pic=" + $p.pic)
        }
    }
}

"`n4. a picture that is gone falls back, and corrects what was saved"
Stop-Process -Id $pidv -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
Remove-Item "profiles\$Profile\startbackground.img" -Force -ErrorAction SilentlyContinue
$json = Get-Content "profiles\$Profile\settings.json" -Raw | ConvertFrom-Json
$json.startBackground = 'Your picture'
[System.IO.File]::WriteAllText((Join-Path $kit "profiles\$Profile\settings.json"), ($json | ConvertTo-Json -Compress))
& .\launch-quiet.ps1 -Profile $Profile -Port $Port | Out-Null
$pidv = [int](Get-Content pid.txt)
Start-Sleep -Seconds 5
$p = Page
Check "the dome is drawn instead" ($p.bg -eq 'glasshouse' -and $p.sky -gt 1000) ($p.bg + " sky=" + $p.sky)
Check "and the store no longer claims a picture" ((Saved) -eq 'Glasshouse') (Saved)

"`n--- nothing should have gone wrong ---"
$iss = "profiles\$Profile\user.data.improvementcollection\issues.jsonl"
# Step 4 force-closes Aviary on purpose, so the relaunch rightly reports an unclean exit.
if (Test-Path $iss) {
    foreach ($line in Get-Content $iss) {
        if ($line -match "unclean-exit") { "  (expected) unclean exit - step 4 force-closed Aviary" }
        else { "  UNEXPECTED: $line" }
    }
} else { "  no issues.jsonl" }
"`n$pass passed, $fail failed"
Stop-Process -Id $pidv -Force -ErrorAction SilentlyContinue
if ($fail -gt 0) { exit 1 }
