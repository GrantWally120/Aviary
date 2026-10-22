# Map the minimise-during-startup crash: launch, minimise after N ms, see whether Aviary survives
# and how far its diagnostics got. Scratch profiles only.
# -RestoreAfterMs n: n ms after minimising, restore it WITHOUT activating (then park it at the back
# of the z-order) and check it is still alive 3 s later — the page gets the focus it was owed then.
param([int[]]$Delays = @(0, 500, 1000, 2000), [int]$RestoreAfterMs = 0)

Add-Type @'
using System; using System.Runtime.InteropServices;
public class WMin4 { public delegate bool CB(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
 [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f); }
'@ -ErrorAction SilentlyContinue

$sp = $PSScriptRoot
. "$PSScriptRoot\default-exe.ps1"

function Close-Test {
  foreach ($t in (Get-Process Aviary -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$sp*" })) {
    $ids = $t.Id
    [WMin4]::EnumWindows({ param($h, $l) $wp = 0; [void][WMin4]::GetWindowThreadProcessId($h, [ref]$wp); if ($wp -eq $ids -and [WMin4]::IsWindowVisible($h)) { [void][WMin4]::PostMessage($h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) }; return $true }, [IntPtr]::Zero) | Out-Null
    $t.WaitForExit(10000) | Out-Null
  }
}

Close-Test
foreach ($delay in $Delays) {
  $prof = Join-Path $sp ("profiles\min" + $delay)
  if (Test-Path $prof) { [System.IO.Directory]::Delete($prof, $true) }
  [void][System.IO.Directory]::CreateDirectory($prof)
  [System.IO.File]::WriteAllText((Join-Path $prof "settings.json"), '{"improvementCollection":"true"}')

  $env:AVIARY_PROFILE_DIR = $prof
  $p = Start-Process -FilePath (Get-AviaryDevExe $sp) -PassThru
  $env:AVIARY_PROFILE_DIR = $null

  for ($i = 0; $i -lt 50 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 100; $p.Refresh() }
  Start-Sleep -Milliseconds $delay
  [void][WMin4]::ShowWindow($p.MainWindowHandle, 7)
  if ($RestoreAfterMs -gt 0) {
    Start-Sleep -Milliseconds $RestoreAfterMs
    [void][WMin4]::ShowWindow($p.MainWindowHandle, 4)   # SW_SHOWNOACTIVATE
    [void][WMin4]::SetWindowPos($p.MainWindowHandle, [IntPtr]1, 0, 0, 0, 0, 0x0013)   # to the back, no activation
    Start-Sleep 3
  } else {
    Start-Sleep 7
  }

  $events = Join-Path $prof "user.data.improvementcollection\events.jsonl"
  $kinds = @()
  if (Test-Path $events) { $kinds = Get-Content $events | ForEach-Object { if ($_ -match '"k":"([a-z-]+)"(,"name":"([a-z-]+)")?') { $Matches[1] + ':' + $Matches[3] } } }
  "delay {0,5} ms: alive={1}  events: {2}" -f $delay, (-not $p.HasExited), ($kinds -join ' ')
  Close-Test
}
