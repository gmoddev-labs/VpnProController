param([Parameter(Mandatory)][string]$SourceDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
# Package the supplied transparent PNGs as multi-resolution Windows ICO files.
foreach ($State in 'Connected', 'Disconnected', 'Connecting', 'Error') {
    $Source = [Drawing.Image]::FromFile((Join-Path $SourceDirectory "State=$State.png"))
    try {
        $Frames = foreach ($Size in 16, 20, 24, 32, 48, 256) {
            $Bitmap = [Drawing.Bitmap]::new($Size, $Size)
            $Graphics = [Drawing.Graphics]::FromImage($Bitmap)
            $Stream = [IO.MemoryStream]::new()
            try {
                $Graphics.Clear([Drawing.Color]::Transparent)
                $Graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $Graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $Graphics.DrawImage($Source, 0, 0, $Size, $Size)
                $Bitmap.Save($Stream, [Drawing.Imaging.ImageFormat]::Png)
                [pscustomobject]@{ Size = $Size; Bytes = $Stream.ToArray() }
            } finally { $Stream.Dispose(); $Graphics.Dispose(); $Bitmap.Dispose() }
        }
        $File = [IO.File]::Create((Join-Path $PSScriptRoot "$State.ico"))
        $Writer = [IO.BinaryWriter]::new($File)
        try {
            $Writer.Write([uint16]0); $Writer.Write([uint16]1); $Writer.Write([uint16]$Frames.Count)
            $Offset = 6 + 16 * $Frames.Count
            foreach ($Frame in $Frames) {
                $Dimension = if ($Frame.Size -eq 256) { 0 } else { $Frame.Size }
                $Writer.Write([byte]$Dimension); $Writer.Write([byte]$Dimension)
                $Writer.Write([byte]0); $Writer.Write([byte]0)
                $Writer.Write([uint16]1); $Writer.Write([uint16]32)
                $Writer.Write([uint32]$Frame.Bytes.Length); $Writer.Write([uint32]$Offset)
                $Offset += $Frame.Bytes.Length
            }
            foreach ($Frame in $Frames) { $Writer.Write([byte[]]$Frame.Bytes) }
        } finally { $Writer.Dispose() }
    } finally { $Source.Dispose() }
}
