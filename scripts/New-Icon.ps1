$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets'
$null = New-Item -ItemType Directory -Force $assets
$images = @(16, 24, 32, 48, 256) | ForEach-Object {
    $n = $_
    $bitmap = New-Object Drawing.Bitmap $n, $n
    $g = [Drawing.Graphics]::FromImage($bitmap)
    $bg = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(12,19,32))
    $fg = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(93,224,226))
    $pen = New-Object Drawing.Pen $fg, 4
    $png = New-Object IO.MemoryStream
    try {
        $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.ScaleTransform($n / 64.0, $n / 64.0)
        $g.FillEllipse($bg, 2,2,60,60)
        $g.DrawRectangle($pen, 12,20,35,24)
        $g.FillRectangle($fg, 48,26,5,12)
        $g.FillRectangle($fg, 17,25,18,14)
        $bitmap.Save($png, [Drawing.Imaging.ImageFormat]::Png)
        [pscustomobject]@{ Size = $n; Data = $png.ToArray() }
    } finally { $png.Dispose(); $pen.Dispose(); $fg.Dispose(); $bg.Dispose(); $g.Dispose(); $bitmap.Dispose() }
}
$stream = [IO.File]::Create((Join-Path $assets 'BlueGauge.ico'))
$writer = New-Object IO.BinaryWriter $stream
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($entry in $images) {
        $writer.Write([byte]($entry.Size % 256)); $writer.Write([byte]($entry.Size % 256))
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$entry.Data.Length); $writer.Write([uint32]$offset)
        $offset += $entry.Data.Length
    }
    foreach ($entry in $images) { $writer.Write([byte[]]$entry.Data) }
} finally { $writer.Dispose() }
