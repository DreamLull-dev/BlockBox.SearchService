param(
    # High-resolution master image (1024x1024 or larger, square PNG)
    [Parameter(Mandatory = $true)][string] $Source,
    # The packaging project's Images folder, for example ...\BlockBox.Package\Images
    [Parameter(Mandatory = $true)][string] $Destination
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sourcePath = (Resolve-Path -LiteralPath $Source).Path
$destinationPath = (Resolve-Path -LiteralPath $Destination).Path
$master = [System.Drawing.Image]::FromFile($sourcePath)

function Save-ScaledIcon([int] $width, [int] $height, [string] $fileName) {
    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.DrawImage($master, 0, 0, $width, $height)
    $graphics.Dispose()

    $target = Join-Path $destinationPath $fileName
    $bitmap.Save($target, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    Write-Host ("  {0}  {1}x{2}" -f $fileName, $width, $height)
}

Write-Host "Generating package icon assets from $sourcePath"

# Square44x44Logo: base size plus every scale (Windows picks the best match for the current DPI)
$logo44 = @(
    @(44,  'Square44x44Logo.png'),
    @(55,  'Square44x44Logo.scale-125.png'),
    @(66,  'Square44x44Logo.scale-150.png'),
    @(88,  'Square44x44Logo.scale-200.png'),
    @(176, 'Square44x44Logo.scale-400.png')
)
foreach ($entry in $logo44) { Save-ScaledIcon $entry[0] $entry[0] $entry[1] }

# Taskbar / Start menu "unplated" icons need one file per target size; otherwise high DPI scales a small image up and looks blurry.
foreach ($size in @(16, 20, 24, 32, 40, 48, 64, 96, 256)) {
    Save-ScaledIcon $size $size ("Square44x44Logo.targetsize-{0}_altform-unplated.png" -f $size)
}

# Square150x150Logo
$logo150 = @(
    @(150, 'Square150x150Logo.png'),
    @(188, 'Square150x150Logo.scale-125.png'),
    @(225, 'Square150x150Logo.scale-150.png'),
    @(300, 'Square150x150Logo.scale-200.png'),
    @(600, 'Square150x150Logo.scale-400.png')
)
foreach ($entry in $logo150) { Save-ScaledIcon $entry[0] $entry[0] $entry[1] }

# StoreLogo
$storeLogo = @(
    @(50,  'StoreLogo.png'),
    @(63,  'StoreLogo.scale-125.png'),
    @(75,  'StoreLogo.scale-150.png'),
    @(100, 'StoreLogo.scale-200.png'),
    @(200, 'StoreLogo.scale-400.png')
)
foreach ($entry in $storeLogo) { Save-ScaledIcon $entry[0] $entry[0] $entry[1] }

$master.Dispose()
Write-Host "Done. Point Package.appxmanifest at the base names: Images\Square44x44Logo.png, Images\Square150x150Logo.png, Images\StoreLogo.png"
