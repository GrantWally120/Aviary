# Drag a finished download out of the popover into Explorer (M5 stage 2).
#
# DESKTOP INPUT - guard.ps1 must pass (30 s). Opens an Explorer window on a SCRATCH folder, parks it
# beside the test window (never over it), lifts the test window topmost without activating it, opens
# the popover through UI Automation, finds the row's screen rectangle through UI Automation, and drags
# it with mouse_event (SetCursorPos does not drive WinUI's pointer pipeline - HANDOFF section 5).
# Then: a COPY must be in the scratch folder and the original must still be where it was. Closes the
# Explorer window, puts the cursor back and hands the foreground back.
#
#   drag-out-test.ps1 [-Port 9386]
param([int]$Port = 9386)
$kit = $PSScriptRoot
Set-Location $kit
$env:CDP_PORT = "$Port"
& .\guard.ps1 -QuietMs 30000 | Out-Null
if ($LASTEXITCODE -ne 0) { "GUARD: someone is using the PC - not dragging"; exit 3 }

$pass = 0; $fail = 0
function Check($what, $ok, $saw) { if ($ok) { $script:pass++; "  PASS  $what" } else { $script:fail++; "  FAIL  $what  --  saw: $saw" } }

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices; using System.Threading;
public static class DragOut {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out PT p);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int ht, uint f);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EP f, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RC r);
    delegate bool EP(IntPtr h, IntPtr l);
    public struct PT { public int X, Y; }
    public struct RC { public int L, T, R, B; }
    public static IntPtr Explorer(string title) { IntPtr f = IntPtr.Zero; EnumWindows(delegate(IntPtr h, IntPtr l) { if (!IsWindowVisible(h)) return true; var c = new StringBuilder(200); GetClassName(h, c, 200); var t = new StringBuilder(300); GetWindowText(h, t, 300); if (c.ToString() == "CabinetWClass" && t.ToString().StartsWith(title)) f = h; return true; }, IntPtr.Zero); return f; }
    public static IntPtr Biggest(int pid) { IntPtr best = IntPtr.Zero; long a = -1; EnumWindows(delegate(IntPtr h, IntPtr l) { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) { RC r; GetWindowRect(h, out r); long x = (long)(r.R - r.L) * (r.B - r.T); if (x > a) { a = x; best = h; } } return true; }, IntPtr.Zero); return best; }
    static void Move(int x, int y) { int w = GetSystemMetrics(0), h = GetSystemMetrics(1); mouse_event(0x8001, x * 65535 / (w - 1), y * 65535 / (h - 1), 0, IntPtr.Zero); }
    public static void Drag(int x1, int y1, int x2, int y2) {
        Move(x1, y1); Thread.Sleep(150);
        mouse_event(0x2, 0, 0, 0, IntPtr.Zero); Thread.Sleep(150);
        for (int i = 1; i <= 40; i++) { Move(x1 + (x2 - x1) * i / 40, y1 + (y2 - y1) * i / 40); Thread.Sleep(25); }
        Thread.Sleep(300); mouse_event(0x4, 0, 0, 0, IntPtr.Zero);
    }
}
'@

$files = Join-Path $kit "profiles\dldrag-files"
$target = Join-Path $kit "profiles\dldrag-target"
foreach ($d in @($files, $target)) { if (Test-Path $d) { Remove-Item $d -Recurse -Force }; New-Item -ItemType Directory -Force $d | Out-Null }
[IO.File]::WriteAllBytes("$files\Holiday photos.zip", (New-Object byte[] 4200000))
$dir = Join-Path $kit "profiles\dldrag"
if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
New-Item -ItemType Directory -Force $dir | Out-Null
$now = [DateTimeOffset]::UtcNow.ToString("o")
[IO.File]::WriteAllText("$dir\downloads.json", "[{`"Id`":`"a`",`"FileName`":`"Holiday photos.zip`",`"FilePath`":" + (ConvertTo-Json "$files\Holiday photos.zip") + ",`"Url`":`"https://example.com/a.zip`",`"State`":`"Completed`",`"ReceivedBytes`":4200000,`"TotalBytes`":4200000,`"StartedUtc`":`"$now`",`"FinishedUtc`":`"$now`"}]")
[IO.File]::WriteAllText("$dir\settings.json", '{"improvementCollection":"true","startNews":"false","listsAutoUpdate":"Off","downloadDir":' + (ConvertTo-Json $files) + '}')
& .\launch-quiet.ps1 -Profile dldrag -Port $Port | Out-Null
$pidv = [int](Get-Content pid.txt); Start-Sleep 4

$before = [DragOut]::GetForegroundWindow()
$cursor = New-Object DragOut+PT; [void][DragOut]::GetCursorPos([ref]$cursor)
Start-Process explorer.exe -ArgumentList "`"$target`""
$ex = [IntPtr]::Zero
for ($i = 0; $i -lt 20 -and $ex -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 400; $ex = [DragOut]::Explorer("dldrag-target") }
Check "a scratch Explorer window opened" ($ex -ne [IntPtr]::Zero) $ex
if ($ex -ne [IntPtr]::Zero) {
    [void][DragOut]::SetWindowPos($ex, [IntPtr]::Zero, 1500, 120, 900, 640, 0x0044)   # beside the test window, shown
    $win = [DragOut]::Biggest($pidv)
    [void][DragOut]::SetWindowPos($win, [IntPtr](-1), 0, 0, 0, 0, 0x13)             # topmost, no activation
    Start-Sleep -Milliseconds 400
    & .\uia.ps1 -ProcessId $pidv -Id DownloadsButton | Out-Null; Start-Sleep 1.2
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $byPid = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $pidv)
    $row = $null
    foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children, $byPid)) {
        $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "Holiday photos.zip, 4.2 MB")
        $hit = $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($hit) { $row = $hit }
    }
    Check "the row is on screen" ($null -ne $row) ""
    if ($row) {
        $rc = $row.Current.BoundingRectangle
        $x1 = [int]($rc.X + 60); $y1 = [int]($rc.Y + $rc.Height / 2)
        [DragOut]::Drag($x1, $y1, 1950, 440)
        Set-Content -Path (Join-Path $kit "lastinput.txt") -Value ([uint32]([Environment]::TickCount -band 0xFFFFFFFF))
        Start-Sleep 2
        $copy = Get-Item (Join-Path $target "Holiday photos.zip") -ErrorAction SilentlyContinue
        Check "dropped in Explorer, the file arrives" ($copy -and $copy.Length -eq 4200000) ((Get-ChildItem $target | ForEach-Object { "$($_.Name)=$($_.Length)" }) -join ', ')
        Check "as a COPY - the original is still where the list says" ((Get-Item "$files\Holiday photos.zip" -ErrorAction SilentlyContinue).Length -eq 4200000) ""
        $ev = Select-String -Path "profiles\dldrag\user.data.improvementcollection\events.jsonl" -Pattern 'download-drag'
        Check "and Aviary recorded the drag" (@($ev).Count -eq 1) @($ev).Count
    }
    [void][DragOut]::PostMessage($ex, 0x10, [IntPtr]::Zero, [IntPtr]::Zero)
    [void][DragOut]::SetWindowPos($win, [IntPtr](-2), 0, 0, 0, 0, 0x13)
}
[void][DragOut]::SetCursorPos($cursor.X, $cursor.Y)
Stop-Process -Id $pidv -Force -ErrorAction SilentlyContinue
Start-Sleep 1
"foreground back with the window that had it: " + ([DragOut]::GetForegroundWindow() -eq $before)
"`n$pass passed, $fail failed"
if ($fail -gt 0) { exit 1 }
