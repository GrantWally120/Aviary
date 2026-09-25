# Measures candidate engine switches for the Experimental menu (Alpha Build 5 M6) BEFORE any of
# them is built: each switch in its own fresh scratch instance, against a control with none.
# No desktop input (launch-quiet + DevTools). Prints one line per run.
#
#   switch-probe.ps1 [-Port 9333] [-Switches "","--disable-smooth-scrolling",...]
param(
    [int]$Port = 9333,
    [string[]]$Switches = @(
        "",                                             # the control: Aviary as it is
        "--disable-frame-rate-limit",                   # "as smooth as possible"
        "--disable-gpu-vsync --disable-frame-rate-limit",
        "--disable-smooth-scrolling",
        "--enable-features=WebContentsForceDark",
        "--blink-settings=forceDarkModeEnabled=true",
        "--disable-accelerated-video-decode"
    ),
    [string]$Exe = ""
)
$kit = $PSScriptRoot
Set-Location $kit
$env:CDP_PORT = "$Port"
Add-Type -AssemblyName System.Drawing
try { Invoke-WebRequest -UseBasicParsing "http://127.0.0.1:8797/raf" -TimeoutSec 2 | Out-Null }
catch { Start-Process node -ArgumentList "expsite.js" -WorkingDirectory $kit -WindowStyle Hidden | Out-Null; Start-Sleep 2 }

$n = 0
foreach ($sw in $Switches) {
    $n++
    $line = & .\launch-quiet.ps1 -Profile "swp$n" -Fresh -Port $Port -Exe $Exe -ExtraArgs $sw `
        -SettingsJson '{"startNews":"false","listsAutoUpdate":"Off","improvementCollection":"true"}'
    $pidv = if ("$line" -match 'TEST PID (\d+)') { [int]$Matches[1] } else { 0 }
    Start-Sleep 4
    $raw = node switch-probe.js all
    Stop-Process -Id $pidv -Force -ErrorAction SilentlyContinue
    Start-Sleep 2
    try { $r = $raw | ConvertFrom-Json } catch { "[$sw] probe failed: $raw"; continue }
    $px = "?"
    if ($r.darkPng) {
        $bmp = [System.Drawing.Bitmap]::new([System.IO.MemoryStream]::new([Convert]::FromBase64String($r.darkPng)))
        $c = $bmp.GetPixel(1, 1); $px = "rgb($($c.R),$($c.G),$($c.B))"; $bmp.Dispose()
    }
    $name = if ($sw) { $sw } else { "(control - no switch)" }
    "{0}" -f $name
    "    fps {0} | page background {1}, prefers dark {2}" -f $r.fps, $px, $r.darkScheme
    "    Page Down: {0} positions over {1} frames, ended at {2}, first {3} | DevTools wheel: {4} positions, ended at {5}" -f `
        $r.scroll.distinct, $r.scroll.frames, $r.scroll.end, ($r.scroll.first -join ','), $r.wheel.distinct, $r.wheel.end
    "    h264 1080p: {0} | vp9 1080p: {1}" -f ($r.video.h264 | ConvertTo-Json -Compress), ($r.video.vp9 | ConvertTo-Json -Compress)
}
