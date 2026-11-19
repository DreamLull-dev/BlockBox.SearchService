$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this uninstaller from an elevated PowerShell session.'
}

$serviceName = 'BlockBoxGlobalSearch'
& sc.exe stop $serviceName 2>$null | Out-Null
& sc.exe delete $serviceName 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not remove BlockBoxGlobalSearch service.' }
Write-Host 'Service removed. Search index data was kept in ProgramData\BlockBox\Search.'
