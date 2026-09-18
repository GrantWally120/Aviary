# Bug 50's recorder (NativeCrash.cs): launch the test build on a scratch profile with collection on,
# let AVIARY_CRASH_TEST=native call abort() on the UI thread, and show what it left behind —
# crash.log beside the profile and the report in crashes/. The window is parked at the back of the
# z-order the moment it appears (no activation), so it stays out of Grant's way.
param([string]$Mode = "native", [string]$Profile = "native1", [int]$WaitSeconds = 12)

Add-Type @'
using System; using System.Runtime.InteropServices;
public class NcWin { public delegate bool CB(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
 [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h); }
'@ -ErrorAction SilentlyContinue

$sp = $PSScriptRoot
& "$sp\guard.ps1"; if ($LASTEXITCODE -ne 0) { exit 3 }

$prof = Join-Path $sp ("profiles\" + $Profile)
if (Test-Path $prof) { [System.IO.Directory]::Delete($prof, $true) }
[void][System.IO.Directory]::CreateDirectory($prof)
[System.IO.File]::WriteAllText((Join-Path $prof "settings.json"), '{"improvementCollection":"true"}')

$env:AVIARY_PROFILE_DIR = $prof
$env:AVIARY_CRASH_TEST = $Mode
$env:AVIARY_TEST_NATIVE_DETAIL = "1"
$p = Start-Process -FilePath (Join-Path $sp "pub\Aviary.exe") -PassThru
$env:AVIARY_PROFILE_DIR = $null; $env:AVIARY_CRASH_TEST = $null; $env:AVIARY_TEST_NATIVE_DETAIL = $null

# Park every visible window of the process at the bottom, without activating it.
for ($i = 0; $i -lt 40; $i++) {
  Start-Sleep -Milliseconds 100
  $parked = 0
  [NcWin]::EnumWindows({ param($h, $l) $wp = 0; [void][NcWin]::GetWindowThreadProcessId($h, [ref]$wp)
    if ($wp -eq $p.Id -and [NcWin]::IsWindowVisible($h)) { [void][NcWin]::SetWindowPos($h, [IntPtr]1, 0, 0, 0, 0, 0x0013); $script:parked++ }; return $true }, [IntPtr]::Zero) | Out-Null
  if ($parked -gt 0) { break }
}

$exited = $p.WaitForExit($WaitSeconds * 1000)
"pid $($p.Id): exited=$exited" + $(if ($exited) { " code=0x{0:x8}" -f $p.ExitCode } else { "" })
if (-not $exited) { $p.Kill() }

$log = Join-Path $prof "crash.log"
"--- crash.log:"
if (Test-Path $log) { Get-Content $log -Raw } else { "(none)" }
"--- crashes/:"
Get-ChildItem (Join-Path $prof "user.data.improvementcollection\crashes") -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName; Get-Content $_.FullName -Raw }
