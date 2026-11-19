<#
.SYNOPSIS
    Build the zero-cost distribution archive for the Block Box search service (zip + scripts).

.DESCRIPTION
    Publishes the service as a self-contained app, adds Install-SearchService.ps1 and
    Uninstall-SearchService.ps1, and writes a zip that can be attached to a GitHub release.
    Users pick that zip in Block Box ("I already downloaded it...") or run the script
    manually from an elevated PowerShell.

.PARAMETER Version
    Version used in the archive name, for example 1.0.0.0.

.PARAMETER OutputDirectory
    Output directory; defaults to <this folder>\out.
#>
param(
    [string] $Version = '1.0.0.0',
    [string] $OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Version must have four numeric parts (for example 1.0.0.0); got: $Version"
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$publishScript = Join-Path $repoRoot 'BlockBox.SearchService\Publish-SearchService.ps1'
Write-Host "Publishing the service: $publishScript"
& $publishScript
if ($LASTEXITCODE -ne 0) { throw 'Search service publish failed.' }

$publishDirectory = Join-Path $repoRoot 'BlockBox.SearchService\publish\win-x64'
if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory 'BlockBox.SearchService.exe'))) {
    throw "The publish folder is incomplete: $publishDirectory"
}

$stagingRoot = Join-Path $env:TEMP 'BlockBoxServiceZip'
if (Test-Path -LiteralPath $stagingRoot) { Remove-Item -LiteralPath $stagingRoot -Recurse -Force }
$staging = Join-Path $stagingRoot 'BlockBox.SearchService'
New-Item -ItemType Directory -Force -Path $staging | Out-Null
Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $staging -Recurse -Force
Copy-Item -Path (Join-Path $repoRoot 'BlockBox.SearchService\Install-SearchService.ps1') -Destination $staging -Force
Copy-Item -Path (Join-Path $repoRoot 'BlockBox.SearchService\Uninstall-SearchService.ps1') -Destination $staging -Force

$readme = @'
Block Box Search Service
========================

Install (administrator):
  1. Right-click Install-SearchService.ps1 > Run with PowerShell (as administrator), or
  2. In Block Box, open the global search window and choose "I already downloaded it...",
     then select this zip.

Uninstall:
  Settings > Apps > "Block Box Search Service", or run Uninstall-SearchService.ps1
  from an elevated PowerShell. The search index in %ProgramData%\BlockBox\Search is kept
  unless you pass -RemoveData.
'@
Set-Content -LiteralPath (Join-Path $staging 'README.txt') -Value $readme -Encoding ascii

$outputRoot = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { Join-Path $PSScriptRoot 'out' } else { $OutputDirectory }
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$zipPath = Join-Path $outputRoot "BlockBox.SearchService_${Version}_x64.zip"
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host ''
Write-Host "Service archive created: $zipPath"
Write-Host 'Upload it to a GitHub release and point SupportInfo.ServiceDownloadUrl at the release page.'
