# Builds rabbidlab.ico and rabbidlab-dev.ico from favicon.svg and favicon-dev.svg.
# Edge draws each SVG at 512 px; the sizes Windows asks for are scaled down from that.
#   powershell -File Tools\make-icons.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$server = Join-Path $root 'Src\RabbidLab.Server'
$work = Join-Path $root 'artifacts\icons'
New-Item -ItemType Directory -Force $work | Out-Null
$edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
if (-not (Test-Path $edge)) { $edge = "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe" }

function Draw([string]$svg, [string]$png) {
    $page = Join-Path $work 'page.html'
    $uri = ([Uri](Join-Path $server "wwwroot\$svg")).AbsoluteUri
    [IO.File]::WriteAllText($page, "<!doctype html><style>html,body{margin:0;background:transparent}img{display:block;width:512px;height:512px}</style><img src=`"$uri`">")
    if (Test-Path $png) { Remove-Item $png -Force }
    $run = Start-Process $edge -PassThru -Wait -ArgumentList '--headless', '--disable-gpu', '--hide-scrollbars', '--allow-file-access-from-files',
        '--default-background-color=00000000', '--window-size=512,512', "--user-data-dir=`"$(Join-Path $work 'edge')`"", "--screenshot=`"$png`"", "`"$(([Uri]$page).AbsoluteUri)`""
    if (-not (Test-Path $png)) { throw "Edge did not draw $svg (exit $($run.ExitCode))." }
}

function Scaled([Drawing.Bitmap]$from, [int]$size) {
    $to = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($to)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.CompositingQuality = 'HighQuality'
    $g.DrawImage($from, (New-Object Drawing.Rectangle 0, 0, $size, $size), 0, 0, $from.Width, $from.Height, [Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    $to
}

# One picture as an icon entry: PNG at 256, else a 32-bit bitmap with an empty mask.
function Entry([Drawing.Bitmap]$bitmap) {
    $ms = New-Object IO.MemoryStream
    $size = $bitmap.Width
    if ($size -ge 256) {
        $bitmap.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
        return , $ms.ToArray()
    }
    $w = New-Object IO.BinaryWriter $ms
    $w.Write([int]40); $w.Write([int]$size); $w.Write([int]($size * 2)); $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) {
            $p = $bitmap.GetPixel($x, $y)
            $w.Write([byte]$p.B); $w.Write([byte]$p.G); $w.Write([byte]$p.R); $w.Write([byte]$p.A)
        }
    }
    $row = [int]([Math]::Ceiling($size / 32.0) * 4)
    $w.Write((New-Object byte[] ($row * $size)))
    $w.Flush()
    , $ms.ToArray()
}

function Pack([string]$png, [string]$ico) {
    $sizes = 16, 20, 24, 32, 40, 48, 64, 256
    $from = New-Object Drawing.Bitmap $png
    $entries = foreach ($s in $sizes) { $b = Scaled $from $s; , (Entry $b); $b.Dispose() }
    $from.Dispose()
    $ms = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $ms
    $w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$sizes.Count)
    $at = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0); $w.Write([int16]1); $w.Write([int16]32)
        $w.Write([int]$entries[$i].Length); $w.Write([int]$at)
        $at += $entries[$i].Length
    }
    foreach ($e in $entries) { $w.Write($e) }
    $w.Flush()
    [IO.File]::WriteAllBytes($ico, $ms.ToArray())
    "$ico  ($($ms.Length) bytes)"
}

foreach ($pair in @(@('favicon.svg', 'rabbidlab.ico'), @('favicon-dev.svg', 'rabbidlab-dev.ico'))) {
    $png = Join-Path $work ($pair[0] -replace '\.svg$', '.png')
    Draw $pair[0] $png
    Pack $png (Join-Path $server $pair[1])
}
