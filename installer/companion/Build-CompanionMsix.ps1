<#
.SYNOPSIS
    Build (and optionally sign) the companion MSIX for the Block Box search service.

.DESCRIPTION
    Publishes BlockBox.SearchService as a self-contained app and packs it, together with
    AppxManifest.xml, into a standalone MSIX. The package is meant for the app build that
    does not ship the service (for example the Microsoft Store version): the user installs
    this companion package separately when whole-disk search is wanted.
      - Installing requires administrator rights (packaged services cannot be installed per-user).
      - The service is registered and removed together with the package.

.PARAMETER Version
    Four-part version, for example 1.0.0.0. Must increase to upgrade.

.PARAMETER Publisher
    Certificate subject; must match the signing certificate Subject exactly (for example CN=BlockBox).

.PARAMETER CertPath
    PFX certificate path. Omit to produce an unsigned package (local testing only).

.PARAMETER CertPassword
    PFX password.

.PARAMETER CertThumbprint
    Certificate thumbprint (USB token / certificate store when the private key cannot be exported).

.PARAMETER OutputDirectory
    Output directory; defaults to <this folder>\out.
#>
param(
    [string] $Version = '1.0.0.0',
    [string] $Publisher = 'CN=BlockBox',
    [string] $CertPath = '',
    [string] $CertPassword = '',
    [string] $CertThumbprint = '',
    [string] $OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Version must have four numeric parts (for example 1.0.0.0); got: $Version"
}
if ($Version -eq '0.0.0.0') {
    throw 'Version cannot be 0.0.0.0'
}
if ([string]::IsNullOrWhiteSpace($Publisher)) {
    throw 'Publisher is required and must match the signing certificate subject.'
}

function Find-SdkTool([string] $name) {
    # 1) Microsoft.Windows.SDK.BuildTools from NuGet (many dev machines lack the full Windows SDK).
    $nugetRoot = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools'
    if (Test-Path -LiteralPath $nugetRoot) {
        $candidate = Get-ChildItem -LiteralPath $nugetRoot -Recurse -Filter "$name.exe" -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($candidate) { return $candidate.FullName }
    }

    # 2) Windows SDK install directory.
    foreach ($root in @("${env:ProgramFiles(x86)}\Windows Kits\10\bin", "$env:ProgramFiles\Windows Kits\10\bin")) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        $candidate = Get-ChildItem -LiteralPath $root -Recurse -Filter "$name.exe" -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($candidate) { return $candidate.FullName }
    }

    # 3) PATH.
    $command = Get-Command $name -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    throw "Cannot find $name.exe: install the Windows SDK (or restore Microsoft.Windows.SDK.BuildTools)."
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$publishScript = Join-Path $repoRoot 'BlockBox.SearchService\Publish-SearchService.ps1'
Write-Host "Publishing the service: $publishScript"
& $publishScript
if ($LASTEXITCODE -ne 0) { throw 'Search service publish failed.' }

$publishDirectory = Join-Path $repoRoot 'BlockBox.SearchService\publish\win-x64'
$serviceExe = Join-Path $publishDirectory 'BlockBox.SearchService.exe'
if (-not (Test-Path -LiteralPath $serviceExe)) {
    throw "The publish folder is incomplete: $serviceExe is missing."
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
        throw "Publisher does not match the certificate subject: '$($certificate.Subject)' vs '$Publisher'."
    }
    $sign = $true
}
elseif (-not [string]::IsNullOrWhiteSpace($CertThumbprint)) {
    # Certificate in the store / on a USB token (private key cannot be exported to a PFX).
    $thumb = $CertThumbprint.Replace(' ', '')
    $certificate = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -eq $thumb } |
        Select-Object -First 1
    if (-not $certificate) {
        throw "No certificate with thumbprint $CertThumbprint was found in the local certificate stores."
    }
    if ($certificate.Subject -ne $Publisher) {
        throw "Publisher does not match the certificate subject: '$($certificate.Subject)' vs '$Publisher'."
    }
    $sign = $true
}

$outputRoot = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { Join-Path $PSScriptRoot 'out' } else { $OutputDirectory }
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$msixPath = Join-Path $outputRoot "BlockBox.SearchService_${Version}_x64.msix"
if (Test-Path -LiteralPath $msixPath) { Remove-Item -LiteralPath $msixPath -Force }

$makeAppx = Find-SdkTool 'makeappx'
Write-Host "Packing: $makeAppx"
& $makeAppx pack /d $layout /p $msixPath /o
if ($LASTEXITCODE -ne 0) { throw 'makeappx failed.' }

if ($sign) {
    $signTool = Find-SdkTool 'signtool'
    Write-Host "Signing: $signTool"
    if (-not [string]::IsNullOrWhiteSpace($CertThumbprint)) {
        & $signTool sign /fd SHA256 /sha1 ($CertThumbprint.Replace(' ', '')) $msixPath
    }
    else {
        & $signTool sign /fd SHA256 /f $resolvedCert /p $CertPassword $msixPath
    }
    if ($LASTEXITCODE -ne 0) { throw 'signtool failed.' }
}
else {
    Write-Warning 'Unsigned: local testing only. Pass -CertPath or -CertThumbprint for public distribution.'
}

Write-Host ''
Write-Host "Companion MSIX created: $msixPath"
Write-Host 'Install (UAC prompt):'
Write-Host "  Add-AppxPackage `"$msixPath`""
Write-Host 'Uninstall (the service is removed with the package):'
Write-Host '  Get-AppxPackage BlockBox.SearchService | Remove-AppxPackage'
