<#
.SYNOPSIS
    使用 StoreLogo.png 生成多尺寸 ICO 文件。
.DESCRIPTION
    读取一个 PNG 源文件（如 StoreLogo.png），将其缩放到多个尺寸，
    并打包成一个包含 PNG 数据的 .ico 文件。透明通道会保留。
.PARAMETER SourcePath
    源 PNG 文件路径。默认为 .\Assets\StoreLogo.png
.PARAMETER OutputPath
    输出的 .ico 文件路径。默认为 .\Assets\AppIcon.ico
.PARAMETER Sizes
    要包含的图标尺寸（像素）。默认为 16,20,24,32,40,48,64,128,256
.EXAMPLE
    .\Convert-StoreLogoToIco.ps1
.EXAMPLE
    .\Convert-StoreLogoToIco.ps1 -SourcePath "C:\MyApp\Assets\StoreLogo.png" -OutputPath "C:\MyApp\Assets\AppIcon.ico" -Sizes 16,32,48,256
#>

param(
    [string]$SourcePath = ".\Assets\StoreLogo.png",
    [string]$OutputPath = ".\Assets\AppIcon.ico",
    [int[]]$Sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
)

$ErrorActionPreference = "Stop"

# 加载 System.Drawing
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path -LiteralPath $SourcePath)) {
    throw "找不到源文件: $SourcePath"
}

$sourceFullPath = (Resolve-Path -LiteralPath $SourcePath).Path
$outputFullPath = [System.IO.Path]::Combine($PWD.Path, $OutputPath)
$outputDir = [System.IO.Path]::GetDirectoryName($outputFullPath)
if (-not [string]::IsNullOrWhiteSpace($outputDir) -and -not (Test-Path -LiteralPath $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# 加载源图
$sourceImage = [System.Drawing.Image]::FromFile($sourceFullPath)
$sourceWidth = $sourceImage.Width
$sourceHeight = $sourceImage.Height

Write-Host "源图: $sourceFullPath ($sourceWidth x $sourceHeight)"
Write-Host "输出: $outputFullPath"
Write-Host "尺寸: $($Sizes -join ', ')"

# 用于存储每个尺寸的 PNG 字节
$pngDataList = New-Object System.Collections.Generic.List[byte[]]
$actualSizes = New-Object System.Collections.Generic.List[int]

foreach ($size in $Sizes) {
    if ($size -le 0) { continue }
    if ($size -gt 256) {
        Write-Warning "ICO 格式不支持大于 256 的尺寸，已跳过 $size"
        continue
    }

    if ($size -gt $sourceWidth -or $size -gt $sourceHeight) {
        Write-Warning "目标尺寸 $size 大于源图尺寸 $sourceWidth x $sourceHeight，图像会被放大，可能模糊。"
    }

    # 创建目标位图，32bpp ARGB 以保留透明
    $destBitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($destBitmap)
    try {
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)

        # 保持宽高比，居中绘制
        $ratio = [Math]::Min($size / $sourceWidth, $size / $sourceHeight)
        $drawWidth = [int][Math]::Round($sourceWidth * $ratio)
        $drawHeight = [int][Math]::Round($sourceHeight * $ratio)
        $offsetX = [int](($size - $drawWidth) / 2)
        $offsetY = [int](($size - $drawHeight) / 2)

        $graphics.DrawImage($sourceImage, $offsetX, $offsetY, $drawWidth, $drawHeight)
    }
    finally {
        $graphics.Dispose()
    }

    # 将位图保存为 PNG 到内存流
    $ms = New-Object System.IO.MemoryStream
    try {
        $destBitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngBytes = $ms.ToArray()
        $pngDataList.Add($pngBytes)
        $actualSizes.Add($size)
    }
    finally {
        $ms.Dispose()
        $destBitmap.Dispose()
    }
}

$sourceImage.Dispose()

if ($pngDataList.Count -eq 0) {
    throw "没有生成任何图标数据。"
}

# 构建 ICO 文件
$icoStream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($icoStream)

try {
    # ICONDIR 头
    $writer.Write([UInt16]0)          # 保留，必须为 0
    $writer.Write([UInt16]1)          # 类型，1 = 图标
    $writer.Write([UInt16]$pngDataList.Count)  # 图像数量

    # 计算数据偏移：6 字节头 + 每个条目 16 字节
    $dataOffset = 6 + (16 * $pngDataList.Count)

    # 写入每个 ICONDIRENTRY
    for ($i = 0; $i -lt $pngDataList.Count; $i++) {
        $size = $actualSizes[$i]
        $pngBytes = $pngDataList[$i]

        # 宽度/高度：0 表示 256
        $widthByte = if ($size -ge 256) { [byte]0 } else { [byte]$size }
        $heightByte = if ($size -ge 256) { [byte]0 } else { [byte]$size }

        $writer.Write([byte]$widthByte)      # 宽度
        $writer.Write([byte]$heightByte)     # 高度
        $writer.Write([byte]0)               # 调色板颜色数，0 表示无调色板
        $writer.Write([byte]0)               # 保留，必须为 0
        $writer.Write([UInt16]1)             # 颜色平面数，1
        $writer.Write([UInt16]32)            # 位深度，32
        $writer.Write([UInt32]$pngBytes.Length)  # 图像数据大小
        $writer.Write([UInt32]$dataOffset)       # 图像数据偏移

        $dataOffset += $pngBytes.Length
    }

    # 写入所有 PNG 数据
    foreach ($pngBytes in $pngDataList) {
        $writer.Write($pngBytes)
    }

    $writer.Flush()
    [System.IO.File]::WriteAllBytes($outputFullPath, $icoStream.ToArray())
}
finally {
    $writer.Dispose()
    $icoStream.Dispose()
}

Write-Host "已生成 ICO: $outputFullPath" -ForegroundColor Green
Write-Host "包含尺寸: $($actualSizes -join ', ')" -ForegroundColor Green