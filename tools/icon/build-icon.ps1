# Draws Aviary's app icon, in both of its looks, and writes each as a real multi-size .ico
# (Alpha Build 5 M4.6). Checked in so the icon can be REDRAWN rather than hand-edited: change a
# number here, run it, and every size follows.
#
#   powershell -NoProfile -File tools\icon\build-icon.ps1
#
# The mark is Aviary's own, the one already on the Start Page and in Settings: a birdcage dome -
# an arch, four bars, a base line. Two tiles carry it:
#
#   Aviary.ico        the blue tile   - a Windows 11 squircle in Aviary's blue with a top-down
#                                       sheen, the dome in white. This one is compiled into the
#                                       exe, so it is what Explorer and the installer show.
#   Aviary-Glass.ico  the glass tile  - the Liquid Glass capsule as a tile: translucent white,
#                                       a bright rim, the dome in the accent blue.
#
# Settings -> Accessibility -> App Icon switches the window and taskbar between them at runtime
# (AppWindow.SetIcon); the one inside the .exe cannot change while it runs.
#
# No ImageMagick, no NuGet: System.Drawing draws each size, and the ICO container is written by
# hand - a 6-byte header, a 16-byte entry per image, then the images. Every size is stored as a
# PNG, which Windows Vista and later read for any size (older Windows wants BMP for the small
# ones, and Aviary needs Windows 11 anyway).
param([string]$OutDir = (Join-Path $PSScriptRoot "..\..\src\Aviary\Assets"))

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
New-Item -ItemType Directory -Force $OutDir | Out-Null

function New-Squircle([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    # Windows 11 tiles are a rounded square with a generous radius; a path beats DrawRoundedRectangle
    # because it can be both filled and used as a clip.
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc(($x + $w - $d), $y, $d, $d, 270, 90)
    $p.AddArc(($x + $w - $d), ($y + $h - $d), $d, $d, 0, 90)
    $p.AddArc($x, ($y + $h - $d), $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw-Dome($g, [single]$size, $colour, [single]$weight) {
    # The same shape as the SVG in StartPage.cs / InternalPages.cs, scaled from its 24x24 box:
    #   arch  M3 20 V12.6 A9 9 0 0 1 21 12.6 V20      bars at x = 7.6, 12, 16.4
    #   floor M2.2 20 h19.6
    $u = $size / 24.0
    $pen = New-Object System.Drawing.Pen($colour, ($weight * $u))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $left = 3 * $u; $right = 21 * $u; $floor = 20 * $u; $spring = 12.6 * $u
    $r = ($right - $left) / 2
    $g.DrawArc($pen, $left, ($spring - $r), ($r * 2), ($r * 2), 180, 180)
    $g.DrawLine($pen, $left, $spring, $left, $floor)
    $g.DrawLine($pen, $right, $spring, $right, $floor)

    # The bars: thinner, and stopping where the dome's curve does.
    $thin = New-Object System.Drawing.Pen($colour, ([Math]::Max(0.6, $weight * 0.52) * $u))
    $thin.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $thin.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    foreach ($bar in 7.6, 12.0, 16.4) {
        $bx = $bar * $u
        $dx = [Math]::Abs(($bar - 12.0) * $u)
        $top = $spring - [Math]::Sqrt([Math]::Max(0, ($r * $r) - ($dx * $dx)))
        $g.DrawLine($thin, $bx, $top, $bx, $floor)
    }

    $g.DrawLine($pen, (2.2 * $u), $floor, (21.8 * $u), $floor)
    $pen.Dispose(); $thin.Dispose()
}

function New-IconBitmap([int]$px, [string]$look) {
    $bmp = New-Object System.Drawing.Bitmap($px, $px, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # A Windows 11 app icon leaves a little air around its tile.
    $pad = [single]($px * 0.06)
    $side = [single]($px - $pad * 2)
    $radius = [single]($side * 0.235)
    $tile = New-Squircle $pad $pad $side $side $radius

    if ($look -eq 'glass') {
        # Liquid Glass: a pale translucent tile, a brighter top, and a rim that catches the light.
        $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            (New-Object System.Drawing.PointF($pad, $pad)),
            (New-Object System.Drawing.PointF($pad, ($pad + $side))),
            [System.Drawing.Color]::FromArgb(236, 252, 252, 254),
            [System.Drawing.Color]::FromArgb(205, 226, 231, 240))
        $g.FillPath($fill, $tile)
        $fill.Dispose()

        $sheen = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            (New-Object System.Drawing.PointF($pad, $pad)),
            (New-Object System.Drawing.PointF($pad, ($pad + $side * 0.66))),
            [System.Drawing.Color]::FromArgb(150, 255, 255, 255),
            [System.Drawing.Color]::FromArgb(0, 255, 255, 255))
        $sheen.WrapMode = [System.Drawing.Drawing2D.WrapMode]::TileFlipXY
        $old = $g.Clip
        $g.SetClip($tile)
        $g.FillRectangle($sheen, $pad, $pad, $side, ($side * 0.62))
        $g.Clip = $old
        $sheen.Dispose()

        $rim = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(190, 255, 255, 255), [single]([Math]::Max(1.0, $px / 128.0)))
        $g.DrawPath($rim, $tile)
        $rim.Dispose()

        $ink = [System.Drawing.Color]::FromArgb(255, 0, 113, 227)     # Aviary's accent
    }
    else {
        # The blue tile: Aviary's accent, deepening downward, with a soft sheen across the top.
        $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            (New-Object System.Drawing.PointF($pad, $pad)),
            (New-Object System.Drawing.PointF($pad, ($pad + $side))),
            [System.Drawing.Color]::FromArgb(255, 42, 140, 255),
            [System.Drawing.Color]::FromArgb(255, 0, 94, 214))
        $g.FillPath($fill, $tile)
        $fill.Dispose()

        $sheen = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            (New-Object System.Drawing.PointF($pad, $pad)),
            (New-Object System.Drawing.PointF($pad, ($pad + $side * 0.60))),
            [System.Drawing.Color]::FromArgb(70, 255, 255, 255),
            [System.Drawing.Color]::FromArgb(0, 255, 255, 255))
        $sheen.WrapMode = [System.Drawing.Drawing2D.WrapMode]::TileFlipXY
        $old = $g.Clip
        $g.SetClip($tile)
        $g.FillRectangle($sheen, $pad, $pad, $side, ($side * 0.55))
        $g.Clip = $old
        $sheen.Dispose()

        $rim = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(60, 255, 255, 255), [single]([Math]::Max(1.0, $px / 128.0)))
        $g.DrawPath($rim, $tile)
        $rim.Dispose()

        $ink = [System.Drawing.Color]::White
    }

    # The dome sits in the middle of the tile, at about two thirds of its width - the proportion
    # Windows' own icons use, and small enough that the bars survive 16 px.
    $markBox = $side * 0.66
    $offset = ($px - $markBox) / 2
    $state = $g.Save()
    $g.TranslateTransform($offset, ($offset + $markBox * 0.03))
    $weight = if ($px -le 20) { 2.2 } elseif ($px -le 32) { 1.9 } else { 1.6 }
    Draw-Dome $g $markBox $ink $weight
    $g.Restore($state)

    $g.Dispose()
    $tile.Dispose()
    return $bmp
}

function Save-Ico([string]$path, [string]$look) {
    $images = @()
    foreach ($px in $sizes) {
        $bmp = New-IconBitmap $px $look
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $images += , @{ Size = $px; Bytes = $ms.ToArray() }
        $ms.Dispose(); $bmp.Dispose()
    }

    $out = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter($out)
    $w.Write([UInt16]0)            # reserved
    $w.Write([UInt16]1)            # type: icon
    $w.Write([UInt16]$images.Count)

    $offset = 6 + (16 * $images.Count)
    foreach ($img in $images) {
        $w.Write([Byte]($(if ($img.Size -ge 256) { 0 } else { $img.Size })))   # 0 means 256
        $w.Write([Byte]($(if ($img.Size -ge 256) { 0 } else { $img.Size })))
        $w.Write([Byte]0)          # palette
        $w.Write([Byte]0)          # reserved
        $w.Write([UInt16]1)        # colour planes
        $w.Write([UInt16]32)       # bits per pixel
        $w.Write([UInt32]$img.Bytes.Length)
        $w.Write([UInt32]$offset)
        $offset += $img.Bytes.Length
    }

    foreach ($img in $images) { $w.Write($img.Bytes) }
    $w.Flush()
    [System.IO.File]::WriteAllBytes($path, $out.ToArray())
    $w.Dispose(); $out.Dispose()
    "{0}  {1:N0} bytes, {2} sizes" -f (Split-Path $path -Leaf), (Get-Item $path).Length, $images.Count
}

Save-Ico (Join-Path $OutDir "Aviary.ico") 'blue'
Save-Ico (Join-Path $OutDir "Aviary-Glass.ico") 'glass'

# A 256 px PNG of each, for looking at while designing - not shipped.
foreach ($look in 'blue', 'glass') {
    $bmp = New-IconBitmap 256 $look
    $bmp.Save((Join-Path $OutDir ("preview-" + $look + ".png")), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}
"previews written beside them"
