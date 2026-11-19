<#
.SYNOPSIS
    构建（并可签名）Block Box 搜索服务的伴侣 MSIX。

.DESCRIPTION
    把 BlockBox.SearchService 发布成自包含程序，连同 AppxManifest.xml 打成一个独立 MSIX。
    这个包只用于“不含服务的应用（例如 Microsoft Store 版）”在需要整盘搜索时单独安装：
      - 安装需要管理员权限（打包服务必须提权安装）；
      - 服务随包注册、随包卸载，不会残留 sc create 的注册项。

.PARAMETER Version
    四段版本号，例如 1.0.0.0。必须严格递增才能覆盖安装。

.PARAMETER Publisher
    证书主题，必须与签名证书的 Subject 完全一致（例如 CN=BlockBox）。

.PARAMETER CertPath
    PFX 证书路径。不传则生成未签名包（只适合本机测试）。

.PARAMETER CertPassword
    PFX 密码。

.PARAMETER OutputDirectory
    输出目录，默认 <本目录>\out。
#>
param(
    [string] $Version = '1.0.0.0',
    [string] $Publisher = 'CN=BlockBox',
    [string] $CertPath = '',
    [string] $CertPassword = '',
    [string] $OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Version 必须是四段数字（例如 1.0.0.0），当前是：$Version"
}
if ($Version -eq '0.0.0.0') {
    throw 'Version 不能是 0.0.0.0'
}
if ([string]::IsNullOrWhiteSpace($Publisher)) {
    throw 'Publisher 不能为空，必须与签名证书主题一致。'
}

function Find-SdkTool([string] $name) {
    # 1) NuGet 里的 Microsoft.Windows.SDK.BuildTools（很多开发机没装完整 Windows SDK）
    $nugetRoot = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools'
    if (Test-Path -LiteralPath $nugetRoot) {
        $candidate = Get-ChildItem -LiteralPath $nugetRoot -Recurse -Filter "$name.exe" -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($candidate) { return $candidate.FullName }
    }

    # 2) Windows SDK 安装目录
    foreach ($root in @("${env:ProgramFiles(x86)}\Windows Kits\10\bin", "$env:ProgramFiles\Windows Kits\10\bin")) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        $candidate = Get-ChildItem -LiteralPath $root -Recurse -Filter "$name.exe" -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($candidate) { return $candidate.FullName }
    }

    # 3) PATH
    $command = Get-Command $name -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    throw "找不到 $name.exe：请安装 Windows SDK（或还原 Microsoft.Windows.SDK.BuildTools 包）。"
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$publishScript = Join-Path $repoRoot 'BlockBox.SearchService\Publish-SearchService.ps1'
Write-Host "发布服务：$publishScript"
& $publishScript
if ($LASTEXITCODE -ne 0) { throw '服务发布失败。' }

$publishDirectory = Join-Path $repoRoot 'BlockBox.SearchService\publish\win-x64'
$serviceExe = Join-Path $publishDirectory 'BlockBox.SearchService.exe'
if (-not (Test-Path -LiteralPath $serviceExe)) {
    throw "服务发布目录不完整：$serviceExe 不存在。"
}

$layout = Join-Path $env:TEMP 'BlockBoxCompanionMsix\layout'
if (Test-Path -LiteralPath $layout) { Remove-Item -LiteralPath $layout -Recurse -Force }
New-Item -ItemType Directory -Force -Path $layout | Out-Null
Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $layout -Recurse -Force
Copy-Item -Path (Join-Path $PSScriptRoot 'Assets') -Destination $layout -Recurse -Force

$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw -Encoding UTF8
$manifest = $manifest.Replace('__VERSION__', $Version).Replace('__PUBLISHER__', $Publisher)
Set-Content -LiteralPath (Join-Path $layout 'AppxManifest.xml') -Value $manifest -Encoding utf8

$sign = $false
$resolvedCert = ''
if (-not [string]::IsNullOrWhiteSpace($CertPath)) {
    $resolvedCert = (Resolve-Path -LiteralPath $CertPath).Path
    $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($resolvedCert, $CertPassword)
    if ($certificate.Subject -ne $Publisher) {
        throw "Publisher 与证书主题不一致：证书是 '$($certificate.Subject)'，参数是 '$Publisher'。"
    }
    $sign = $true
}

$outputRoot = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { Join-Path $PSScriptRoot 'out' } else { $OutputDirectory }
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$msixPath = Join-Path $outputRoot "BlockBox.SearchService_${Version}_x64.msix"
if (Test-Path -LiteralPath $msixPath) { Remove-Item -LiteralPath $msixPath -Force }

$makeAppx = Find-SdkTool 'makeappx'
Write-Host "打包：$makeAppx"
& $makeAppx pack /d $layout /p $msixPath /o
if ($LASTEXITCODE -ne 0) { throw 'makeappx 打包失败。' }

if ($sign) {
    $signTool = Find-SdkTool 'signtool'
    Write-Host "签名：$signTool"
    & $signTool sign /fd SHA256 /f $resolvedCert /p $CertPassword $msixPath
    if ($LASTEXITCODE -ne 0) { throw 'signtool 签名失败。' }
}
else {
    Write-Warning '未签名：只适合本机测试。正式分发请用 -CertPath 传入代码签名证书。'
}

Write-Host ''
Write-Host "伴侣 MSIX 已生成：$msixPath"
Write-Host '安装（会弹 UAC）：'
Write-Host "  Add-AppxPackage `"$msixPath`""
Write-Host '卸载（服务随包删除）：'
Write-Host '  Get-AppxPackage BlockBox.SearchService | Remove-AppxPackage'
