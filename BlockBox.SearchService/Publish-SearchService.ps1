$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'BlockBox.SearchService.csproj'
$publishDirectory = Join-Path $PSScriptRoot 'publish\win-x64'
dotnet publish $project -c Release -r win-x64 --self-contained true -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw 'Search service publish failed.' }
Write-Host "Published service to $publishDirectory"
Write-Host 'Install from an elevated PowerShell session with:'
Write-Host "  .\Install-SearchService.ps1 -ServiceExecutable `"$publishDirectory\BlockBox.SearchService.exe`""
