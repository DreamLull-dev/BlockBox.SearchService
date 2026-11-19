param(
    # Also delete the search index data in %ProgramData%\BlockBox\Search.
    [switch] $RemoveData,

    # Keep the program files in the install directory (by default they are removed after exit).
    [switch] $KeepFiles
)

$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this uninstaller from an elevated PowerShell session.'
}

$serviceName = 'BlockBoxGlobalSearch'
$installDirectory = Join-Path $env:ProgramFiles 'BlockBox\SearchService'
$dataDirectory = Join-Path $env:ProgramData 'BlockBox\Search'
$uninstallEntry = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\BlockBox.SearchService'

# 1) Stop and remove the service. A missing service makes sc return a non-zero code, which is fine here.
& sc.exe stop $serviceName 2>$null | Out-Null
Start-Sleep -Seconds 1
& sc.exe delete $serviceName 2>$null | Out-Null
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -ne $service) {
    throw 'Could not remove BlockBoxGlobalSearch service.'
}

# 2) Remove the "Apps & features" uninstall entry.
if (Test-Path -LiteralPath $uninstallEntry) {
    Remove-Item -LiteralPath $uninstallEntry -Recurse -Force
}

# 3) Optionally delete the index data.
if ($RemoveData) {
    if (Test-Path -LiteralPath $dataDirectory) {
        Remove-Item -LiteralPath $dataDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Host "Removed search index data: $dataDirectory"
}
else {
    Write-Host "Search index data was kept: $dataDirectory"
}

# 4) Delete the install directory. This script may be running from that directory, so let a
#    short-delayed cmd clean it up after PowerShell exits.
$runningFromInstallDirectory = $PSScriptRoot -and
    [string]::Equals((Resolve-Path -LiteralPath $PSScriptRoot).Path, $installDirectory, [StringComparison]::OrdinalIgnoreCase)
if (-not $KeepFiles -and $runningFromInstallDirectory) {
    $cleanup = 'timeout /t 2 /nobreak >nul & rd /s /q "' + $installDirectory + '"'
    Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', $cleanup -WindowStyle Hidden | Out-Null
}
elseif (-not $KeepFiles) {
    # Running from another location (for example the repository): delete the install directory now.
    if (Test-Path -LiteralPath $installDirectory) {
        Remove-Item -LiteralPath $installDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host 'Service removed. To reinstall, run Install-SearchService.ps1 from an elevated session.'
