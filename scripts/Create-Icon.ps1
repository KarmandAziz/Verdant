$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetRoot = Join-Path $PSScriptRoot '..\src\Verdant.App\Assets'
New-Item -ItemType Directory -Force -Path $assetRoot | Out-Null
$svg = @'
<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
<defs><linearGradient id="bg" x2=".8" y2="1"><stop stop-color="#30483c"/><stop offset="1" stop-color="#111b17"/></linearGradient><linearGradient id="v" x2=".5" y2="1"><stop stop-color="#d5ffe0"/><stop offset=".55" stop-color="#9be3ae"/><stop offset="1" stop-color="#49bb80"/></linearGradient></defs>
<rect x="12" y="12" width="232" height="232" rx="50" fill="url(#bg)" stroke="#567762" stroke-width="2"/>
<path d="M52 62H96L127 140L169 43H214L146 208H111Z" fill="url(#v)"/>
<path d="M111 208L127 140L146 208Z" fill="#318862" opacity=".65"/>
</svg>
'@
Set-Content -LiteralPath (Join-Path $assetRoot 'Verdant.svg') -Value $svg -Encoding utf8
$bitmap = [Drawing.Bitmap]::new(1024,1024)
$g = [Drawing.Graphics]::FromImage($bitmap)
$g.SmoothingMode = 'AntiAlias'; $g.ScaleTransform(4,4)
$round = [Drawing.Drawing2D.GraphicsPath]::new()
foreach($arc in @(@(12,12,100,100,180,90),@(144,12,100,100,270,90),@(144,144,100,100,0,90),@(12,144,100,100,90,90))) { $round.AddArc($arc[0],$arc[1],$arc[2],$arc[3],$arc[4],$arc[5]) }
$round.CloseFigure()
$bg = [Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Point]::new(20,10),[Drawing.Point]::new(220,246),[Drawing.ColorTranslator]::FromHtml('#30483c'),[Drawing.ColorTranslator]::FromHtml('#111b17'))
$edge = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#567762'),2)
$g.FillPath($bg,$round); $g.DrawPath($edge,$round)
$points = [Drawing.PointF[]]@([Drawing.PointF]::new(52,62),[Drawing.PointF]::new(96,62),[Drawing.PointF]::new(127,140),[Drawing.PointF]::new(169,43),[Drawing.PointF]::new(214,43),[Drawing.PointF]::new(146,208),[Drawing.PointF]::new(111,208))
$green = [Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Point]::new(75,43),[Drawing.Point]::new(145,208),[Drawing.ColorTranslator]::FromHtml('#d5ffe0'),[Drawing.ColorTranslator]::FromHtml('#49bb80'))
$g.FillPolygon($green,$points)
$fold = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(166,49,136,98))
$g.FillPolygon($fold,[Drawing.PointF[]]@([Drawing.PointF]::new(111,208),[Drawing.PointF]::new(127,140),[Drawing.PointF]::new(146,208)))
$g.Dispose(); $round.Dispose(); $bg.Dispose(); $edge.Dispose(); $green.Dispose(); $fold.Dispose()
$frames = @()
foreach($size in @(16,20,24,32,40,48,64,96,128,256)) {
 $frame = [Drawing.Bitmap]::new($size,$size); $fg = [Drawing.Graphics]::FromImage($frame)
 $fg.InterpolationMode = 'HighQualityBicubic'; $fg.PixelOffsetMode='HighQuality'; $fg.DrawImage($bitmap,0,0,$size,$size); $fg.Dispose()
 $ms = [IO.MemoryStream]::new(); $frame.Save($ms,[Drawing.Imaging.ImageFormat]::Png)
 $frames += @{ Size=$size; Bytes=$ms.ToArray() }; $ms.Dispose(); $frame.Dispose()
}
$bitmap.Save((Join-Path $assetRoot 'Verdant-preview.png'),[Drawing.Imaging.ImageFormat]::Png); $bitmap.Dispose()
$file = [IO.File]::Create((Join-Path $assetRoot 'Verdant.ico')); $writer=[IO.BinaryWriter]::new($file)
try {
 $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
 $offset = 6 + 16*$frames.Count
 foreach($frame in $frames) { $dimension=if($frame.Size -eq 256){0}else{$frame.Size}; $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset); $offset += $frame.Bytes.Length }
 foreach($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose(); $file.Dispose() }
