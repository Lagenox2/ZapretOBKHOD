param(
    [switch]$NoZip
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'src'
$dist = Join-Path $root 'dist'
$assets = Join-Path $root 'assets'
$ico = Join-Path $assets 'app.ico'

Write-Host '=== ZapretOBKHOD build ===' -ForegroundColor Cyan

New-Item -ItemType Directory -Force $assets | Out-Null
New-Item -ItemType Directory -Force $dist | Out-Null

# --- icon -----------------------------------------------------------------
if (-not (Test-Path $ico)) {
    Add-Type -AssemblyName System.Drawing

    $size = 256
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $points = [System.Drawing.Point[]]@(
        (New-Object System.Drawing.Point(128, 20)),
        (New-Object System.Drawing.Point(220, 58)),
        (New-Object System.Drawing.Point(220, 132)),
        (New-Object System.Drawing.Point(128, 236)),
        (New-Object System.Drawing.Point(36, 132)),
        (New-Object System.Drawing.Point(36, 58))
    )

    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point(36, 20)),
        (New-Object System.Drawing.Point(220, 236)),
        [System.Drawing.Color]::FromArgb(11, 108, 251),
        [System.Drawing.Color]::FromArgb(6, 182, 212))
    $g.FillPolygon($gradient, $points)

    $borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 6)
    $borderPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPolygon($borderPen, $points)

    $checkPen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 16)
    $checkPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $checkPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $checkPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $checkPoints = [System.Drawing.Point[]]@(
        (New-Object System.Drawing.Point(78, 130)),
        (New-Object System.Drawing.Point(115, 168)),
        (New-Object System.Drawing.Point(181, 92))
    )
    $g.DrawLines($checkPen, $checkPoints)

    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $png = $ms.ToArray()
    $ms.Dispose()
    $bmp.Dispose()

    $fs = [System.IO.File]::Create($ico)
    $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([UInt16]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]1)
    $bw.Write([Byte]0)
    $bw.Write([Byte]0)
    $bw.Write([Byte]0)
    $bw.Write([Byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$png.Length)
    $bw.Write([UInt32]22)
    $bw.Write($png)
    $bw.Flush()
    $bw.Close()
    $fs.Close()

    Write-Host "Иконка создана: $ico"
}

# --- clean output ---------------------------------------------------------
if (Test-Path $dist) {
    Remove-Item $dist -Recurse -Force
}
New-Item -ItemType Directory -Force $dist | Out-Null
New-Item -ItemType Directory -Force (Join-Path $dist 'engine') | Out-Null

# --- compile --------------------------------------------------------------
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) {
    throw 'Не найден компилятор C# (csc.exe). Установите .NET Framework 4.8.'
}

$sources = @(Get-ChildItem (Join-Path $src '*.cs') | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw 'Не найдены исходники C#.' }

$exe = Join-Path $dist 'ZapretOBKHOD.exe'
$cscArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/utf8output',
    '/codepage:65001',
    ('/out:' + $exe),
    ('/win32icon:' + $ico),
    ('/win32manifest:' + (Join-Path $src 'app.manifest')),
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Web.Extensions.dll'
) + $sources

Write-Host 'Компиляция...'
& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw "Компиляция не удалась (csc код $LASTEXITCODE)." }

# --- payload --------------------------------------------------------------
Copy-Item (Join-Path $root 'engine\*') (Join-Path $dist 'engine') -Recurse -Force
Copy-Item (Join-Path $root 'lists') $dist -Recurse -Force
Copy-Item (Join-Path $root 'README.md') $dist -Force
Copy-Item (Join-Path $root 'ИНСТРУКЦИЯ.txt') $dist -Force -ErrorAction SilentlyContinue

$size = [math]::Round((Get-ChildItem $dist -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 2)
Write-Host "Готово: $exe" -ForegroundColor Green
Write-Host "Размер сборки: $size МБ"

if (-not $NoZip) {
    $zip = Join-Path $root 'ZapretOBKHOD-portable.zip'
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip -CompressionLevel Optimal
    $zsize = [math]::Round((Get-Item $zip).Length / 1MB, 2)
    Write-Host "Архив: $zip ($zsize МБ)" -ForegroundColor Green
}
