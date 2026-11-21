# Build a multi-size Windows icon from the Nexa PNG artwork.
Add-Type -AssemblyName System.Drawing

$assetDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourcePath = Join-Path $assetDirectory 'nexa.png'
$targetPath = Join-Path $assetDirectory 'nexa.ico'
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$source = [System.Drawing.Image]::FromFile($sourcePath)
$entries = New-Object 'System.Collections.Generic.List[object]'

try {
    foreach ($size in $sizes) {
        $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $png = New-Object System.IO.MemoryStream
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($source, (New-Object System.Drawing.Rectangle(0, 0, $size, $size)),
                (New-Object System.Drawing.Rectangle(0, 0, $source.Width, $source.Height)),
                [System.Drawing.GraphicsUnit]::Pixel)
            $bitmap.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
            $entries.Add([pscustomobject]@{ Size = $size; Bytes = $png.ToArray() })
        }
        finally {
            $png.Dispose()
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }
}
finally {
    $source.Dispose()
}

$output = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($output)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$entries.Count)
    $offset = 6 + 16 * $entries.Count
    foreach ($entry in $entries) {
        $edge = if ($entry.Size -eq 256) { 0 } else { $entry.Size }
        $writer.Write([byte]$edge)
        $writer.Write([byte]$edge)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$entry.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $entry.Bytes.Length
    }
    foreach ($entry in $entries) {
        $writer.Write([byte[]]$entry.Bytes)
    }
    [System.IO.File]::WriteAllBytes($targetPath, $output.ToArray())
}
finally {
    $writer.Dispose()
    $output.Dispose()
}
