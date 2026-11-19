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

function Remove-SearchService {
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($null -eq $service) { return $true }

    # 1) Ask the SCM to stop it and give it time to finish.
    if ($service.Status -ne 'Stopped') {
        & sc.exe stop $serviceName 2>$null | Out-Null
        for ($i = 0; $i -lt 30; $i++) {
            Start-Sleep -Seconds 1
            $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
            if ($null -eq $service -or $service.Status -eq 'Stopped') { break }
        }
    }

    # 2) Still stopping: terminate only the process the registration points at.
    if ($service -and $service.Status -eq 'StopPending') {
        $info = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
        if ($info -and $info.ProcessId -gt 0) {
            try { Stop-Process -Id $info.ProcessId -Force -ErrorAction Stop } catch { }
            Start-Sleep -Seconds 2
        }
    }

    # 3) Delete the registration and wait until the SCM forgets about it.
    & sc.exe delete $serviceName 2>$null | Out-Null
    for ($i = 0; $i -lt 15; $i++) {
        Start-Sleep -Seconds 1
        $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
        if ($null -eq $service) { return $true }
    }

    if ($service -and $service.Status -eq 'StopPending') {
        Write-Warning ("$serviceName is stuck in STOP_PENDING (marked for deletion). " +
            'Reboot Windows to finish removing it, then run this script again for the remaining cleanup.')
    }
    return $false
}

$removed = Remove-SearchService
if (-not $removed) {
    Write-Warning 'The service registration could not be removed yet; continuing with the remaining cleanup.'
}

# Remove the "Apps & features" uninstall entry.
if (Test-Path -LiteralPath $uninstallEntry) {
    Remove-Item -LiteralPath $uninstallEntry -Recurse -Force
}

# Optionally delete the index data.
if ($RemoveData) {
    if (Test-Path -LiteralPath $dataDirectory) {
        Remove-Item -LiteralPath $dataDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Host "Removed search index data: $dataDirectory"
}
else {
    Write-Host "Search index data was kept: $dataDirectory"
}

# Delete the install directory. This script may be running from that directory, so let a
# short-delayed cmd clean it up after PowerShell exits.
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
