param(
    [Parameter(Mandatory = $true)]
    [string] $ServiceExecutable,

    # Deletes the old index database so the next start rebuilds it from scratch.
    # Use after an index schema change, or when volumes stay in "indexing" forever.
    [switch] $ResetIndex,

    # Install only; do not start the service.
    [switch] $NoStart
)

$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this installer from an elevated PowerShell session.'
}

$serviceName = 'BlockBoxGlobalSearch'
$installDirectory = Join-Path $env:ProgramFiles 'BlockBox\SearchService'
$serviceExecutablePath = Join-Path $installDirectory 'BlockBox.SearchService.exe'
$dataDirectory = Join-Path $env:ProgramData 'BlockBox\Search'
$serviceLogPath = Join-Path $dataDirectory 'service.log'

# 0) Validate the publish folder: a partially copied layout produces a service that
#    can never start, and "sc start" would still report success.
$resolvedExecutable = (Resolve-Path -LiteralPath $ServiceExecutable).Path
if (-not (Test-Path -LiteralPath $resolvedExecutable -PathType Leaf)) {
    throw "Service executable not found: $resolvedExecutable"
}
$sourceDirectory = Split-Path -Parent $resolvedExecutable
foreach ($required in @('BlockBox.SearchService.dll', 'Microsoft.Data.Sqlite.dll', 'e_sqlite3.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $sourceDirectory $required))) {
        throw "The publish folder is incomplete: $required is missing next to $resolvedExecutable. Run Publish-SearchService.ps1 again."
    }
}

New-Item -ItemType Directory -Force -Path $dataDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $installDirectory | Out-Null

function Wait-ServiceStopped([object] $service, [int] $seconds) {
    try {
        $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds($seconds))
        return $true
    }
    catch {
        $service.Refresh()
        return $service.Status -eq 'Stopped'
    }
}

function Stop-SearchServiceProcesses {
    # Kill leftovers from the install directory only (for example a manual --console run,
    # or a previous service process that never exited and still holds the index database).
    foreach ($process in (Get-Process -Name 'BlockBox.SearchService' -ErrorAction SilentlyContinue)) {
        $path = $null
        try { $path = $process.Path } catch { }
        if ($path -and [string]::Equals($path, $serviceExecutablePath, [StringComparison]::OrdinalIgnoreCase)) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }
}

# 1) Stop and remove the previous service. Database initialization can delay the stop
#    handler, so wait for it and, as a last resort, terminate its own process only.
$existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -ne $existingService) {
    $existingService.Refresh()
    if ($existingService.Status -ne 'Stopped') {
        if ($existingService.Status -ne 'StopPending') {
            try { Stop-Service -Name $serviceName -Force -ErrorAction Stop }
            catch {
                $existingService.Refresh()
                if ($existingService.Status -ne 'StopPending' -and $existingService.Status -ne 'Stopped') { throw }
            }
        }
        $stopWaitSeconds = if ($existingService.Status -eq 'StopPending') { 3 } else { 20 }
        if (-not (Wait-ServiceStopped $existingService $stopWaitSeconds)) {
            # Only terminate the process that the service registration points at.
            $serviceState = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
            $configuredPath = if ($serviceState) { ([string]$serviceState.PathName).Trim('"') } else { '' }
            if (-not $serviceState -or $serviceState.ProcessId -le 0 -or
                -not [string]::Equals($configuredPath, $serviceExecutablePath, [StringComparison]::OrdinalIgnoreCase)) {
                throw 'The search service did not stop in time and its executable path could not be verified. No process was terminated.'
            }
            Stop-Process -Id $serviceState.ProcessId -Force
            $deadline = (Get-Date).AddSeconds(15)
            while ((Get-Date) -lt $deadline) {
                $existingService.Refresh()
                if ($existingService.Status -eq 'Stopped') { break }
                Start-Sleep -Milliseconds 500
            }
            if ($existingService.Status -ne 'Stopped') {
                throw 'The search service process was terminated, but Windows did not report it as stopped.'
            }
        }
    }
    & sc.exe delete $serviceName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not remove the previous service registration.' }
    Start-Sleep -Seconds 2
}
Stop-SearchServiceProcesses

# 2) Deploy the program files.
Copy-Item -Path (Join-Path $sourceDirectory '*') -Destination $installDirectory -Recurse -Force
& icacls.exe $installDirectory /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not protect the search service installation directory.' }

# When the publish folder lives inside a low-integrity sandbox workspace, the copied
# files can carry a "Low" mandatory label; a LocalSystem service started from such an
# executable runs at low integrity and cannot write ProgramData. Normalize to Medium.
& icacls.exe $installDirectory /setintegritylevel '(OI)(CI)M' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not reset the integrity level of the service installation directory.' }

# 3) Record the interactive user SID and lock down the data directory.
Set-Content -LiteralPath (Join-Path $dataDirectory 'client.sid') -Value $identity.User.Value -Encoding ascii
& icacls.exe $dataDirectory /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' "*$($identity.User.Value):(OI)(CI)M" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not apply search service data directory ACL.' }
& icacls.exe $dataDirectory /setintegritylevel '(OI)(CI)M' | Out-Null

# 4) Optional: drop the index so the service rebuilds it on next start.
if ($ResetIndex) {
    $indexFiles = @(Get-ChildItem -LiteralPath $dataDirectory -Filter 'search-index.db*' -File -ErrorAction SilentlyContinue)
    foreach ($file in $indexFiles) { Remove-Item -LiteralPath $file.FullName -Force }
    Write-Host "Removed $($indexFiles.Count) index file(s); the index will be rebuilt on start."
}

# 5) Register the service.
$quotedServicePath = '"' + $serviceExecutablePath + '"'
$createServiceArgs = @(
    'create', $serviceName,
    'binPath=', $quotedServicePath,
    'start=', 'auto',
    'obj=', 'LocalSystem',
    'DisplayName=', 'BlockBox Global Search'
)
& sc.exe @createServiceArgs
if ($LASTEXITCODE -ne 0) { throw 'Could not create BlockBoxGlobalSearch service.' }
& sc.exe description $serviceName 'Indexes local fixed NTFS volumes for BlockBox global search.' | Out-Null

if ($NoStart) {
    Write-Host "Service installed but not started. Start it with: sc.exe start $serviceName"
    return
}

# 6) Start it and verify it actually stays alive: "sc start" returns 0 even when the
#    process crashes right after startup (for example when the index database cannot
#    be opened), which made earlier installs look successful while nothing ran.
& sc.exe start $serviceName | Out-Null
$status = $null
$deadline = (Get-Date).AddSeconds(15)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    $status = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($null -eq $status -or $status.Status -eq 'Running') { break }
}
$status = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -eq $status -or $status.Status -ne 'Running') {
    $tail = ''
    if (Test-Path -LiteralPath $serviceLogPath) { $tail = (Get-Content -LiteralPath $serviceLogPath -Tail 15) -join [Environment]::NewLine }
    throw "The service was created but is not running.`nService log: $serviceLogPath`n$tail"
}

# Watch it for a few more seconds to catch "starts, then exits immediately".
Start-Sleep -Seconds 3
$status.Refresh()
if ($status.Status -ne 'Running') {
    $tail = ''
    if (Test-Path -LiteralPath $serviceLogPath) { $tail = (Get-Content -LiteralPath $serviceLogPath -Tail 15) -join [Environment]::NewLine }
    throw "The service stopped right after starting (state: $($status.Status)).`nService log: $serviceLogPath`n$tail"
}

Write-Host 'BlockBox Global Search service installed and running.'
if (Test-Path -LiteralPath $serviceLogPath) {
    Write-Host ''
    Write-Host "Recent service log ($serviceLogPath):"
    Get-Content -LiteralPath $serviceLogPath -Tail 10 | ForEach-Object { Write-Host "  $_" }
}
else {
    Write-Host "Service log will appear at: $serviceLogPath"
}
