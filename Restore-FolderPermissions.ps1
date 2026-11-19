<#
.SYNOPSIS
    Restore a folder tree to normal permissions: Medium integrity label plus a writable ACL
    for the current user.

.DESCRIPTION
    Files under a folder that carries a "Low" mandatory integrity label are launched at low
    integrity. Such a process can still read user data but cannot write it, which shows up as:

      * "Access to the path ... is denied" for %LOCALAPPDATA% and the package LocalState
      * Microsoft.Data.Sqlite: "attempt to write a readonly database"
      * Windows.Storage: "Error trying to open the explicit application data handle"

    Typical cause: an AI agent / code sandbox marked the project folder Low so that its own
    restricted process could write there. Anything launched from that folder afterwards
    (Visual Studio, the packaged app, the service installer) inherits the low integrity level.

    This script repairs, recursively:
      1. Mandatory integrity label  Low -> Medium
      2. Explicit ownership         (only with -TakeOwnership)
      3. ACL entries for the current user (Modify by default, FullControl with -FullControl)

    Requires an elevated PowerShell session. Use -WhatIf to inspect the current state without
    changing anything; that mode does not require elevation.

    Note: while an AI agent session is allowed to write this folder it may mark it Low again.
    Run this script after the agent has finished, then build and launch the app yourself.

.PARAMETER Path
    Folder to repair. Defaults to the folder that contains this script.

.PARAMETER FullControl
    Grant FullControl instead of Modify to the current user.

.PARAMETER NoAcl
    Only reset the integrity label; leave ACLs untouched.

.PARAMETER TakeOwnership
    Run takeown on the tree first. Use it when the folder is owned by BUILTIN\Administrators
    and the ACL cannot be changed even when elevated.

.PARAMETER ResetAcl
    Reset the ACL of the whole tree to the permissions inherited from the parent folder.
    This removes leftovers injected by agent sandboxes (a DENY entry for Everyone,
    "CodexSandboxUsers", logon-session SIDs such as S-1-4-...). Use it when the script reports
    such leftovers, or when deleting files under the folder is denied for no obvious reason.

.EXAMPLE
    .\Restore-FolderPermissions.ps1 -WhatIf

.EXAMPLE
    .\Restore-FolderPermissions.ps1

.EXAMPLE
    .\Restore-FolderPermissions.ps1 -Path 'D:\Projects\BlockBox' -FullControl
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $Path,
    [switch] $FullControl,
    [switch] $NoAcl,
    [switch] $TakeOwnership,
    [switch] $ResetAcl
)

$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdministrator -and -not $WhatIfPreference) {
    throw 'Run this script from an elevated PowerShell session, or add -WhatIf to inspect the current state only.'
}

# Default target: the folder that contains this script (falls back to the current directory).
if ([string]::IsNullOrWhiteSpace($Path)) {
    $Path = if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) { (Get-Location).Path } else { $PSScriptRoot }
}
if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
    throw "Not a folder: $Path"
}
$target = (Resolve-Path -LiteralPath $Path).Path
$userSid = $identity.User.Value

function Get-IntegrityLabel([string] $itemPath) {
    $lines = @()
    $output = & icacls.exe $itemPath 2>&1
    foreach ($line in $output) {
        if ($line -match 'Mandatory Label') { $lines += $line.Trim() }
    }
    return $lines
}

function Show-IntegrityState([string] $itemPath, [string] $prefix) {
    $labels = @(Get-IntegrityLabel $itemPath)
    if ($labels.Count -eq 0) {
        Write-Host "$prefix(default: Medium)" -ForegroundColor DarkGray
    }
    else {
        foreach ($label in $labels) {
            $color = if ($label -match 'Low') { 'Yellow' } else { 'DarkGray' }
            Write-Host "$prefix$label" -ForegroundColor $color
        }
    }
}

Write-Host ''
Write-Host '=== Before ===' -ForegroundColor Cyan
Write-Host "Folder      : $target"
Write-Host "User        : $($identity.Name) ($userSid)"
Write-Host "Elevated    : $isAdministrator"
Write-Host 'Integrity   :'
Show-IntegrityState $target '  '
Write-Host 'ACL         :'
foreach ($line in @(& icacls.exe $target 2>&1 | Select-Object -First 10)) {
    Write-Host ('  ' + $line.Trim())
}

# Detect leftovers injected by agent sandboxes.
$currentAcl = (& icacls.exe $target 2>&1) -join [Environment]::NewLine
$sandboxLeftovers = @()
if ($currentAcl -match '\(DENY\)') { $sandboxLeftovers += 'DENY entry' }
if ($currentAcl -match 'CodexSandbox|SandboxUsers') { $sandboxLeftovers += 'sandbox group' }
if ($currentAcl -match 'S-1-4-') { $sandboxLeftovers += 'logon-session SID (S-1-4-...)' }
if ($sandboxLeftovers.Count -gt 0) {
    if ($ResetAcl) {
        Write-Host ''
        Write-Host ("Sandbox leftovers will be removed by -ResetAcl: {0}" -f ($sandboxLeftovers -join ', ')) -ForegroundColor DarkGray
    }
    else {
        Write-Host ''
        Write-Warning ("Sandbox leftovers found: {0}. Re-run with -ResetAcl to restore the permissions inherited from the parent folder." -f ($sandboxLeftovers -join ', '))
    }
}

# 1) Optional: take ownership so the ACL/label can always be rewritten.
if ($TakeOwnership) {
    if ($PSCmdlet.ShouldProcess($target, 'takeown /F /R /D Y')) {
        & takeown.exe /F $target /R /D Y | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Warning "takeown returned $LASTEXITCODE." }
    }
}

# 2) Reset the mandatory integrity label to Medium for the whole tree.
if ($PSCmdlet.ShouldProcess($target, 'reset integrity label to Medium (recursive)')) {
    Write-Host ''
    Write-Host 'Resetting integrity label to Medium (this can take a while on large trees)...' -ForegroundColor Cyan
    & icacls.exe $target /setintegritylevel '(OI)(CI)M' /T /C /Q | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "icacls reported $LASTEXITCODE item(s) it could not relabel; the verification below shows what is left."
    }
}

# 3) Optional: remove sandbox leftovers by restoring inherited permissions.
if ($ResetAcl) {
    if ($PSCmdlet.ShouldProcess($target, 'reset ACLs to the permissions inherited from the parent folder (recursive)')) {
        Write-Host ''
        Write-Host 'Resetting ACLs to inherited defaults...' -ForegroundColor Cyan
        & icacls.exe $target /reset /T /C /Q | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "icacls reported $LASTEXITCODE item(s) it could not reset."
        }
    }
}

# 4) Grant the current user write access (Modify by default).
if (-not $NoAcl) {
    if ($PSCmdlet.ShouldProcess($target, 'grant the current user access (recursive)')) {
        Write-Host ''
        Write-Host 'Granting the current user access...' -ForegroundColor Cyan
        $rights = if ($FullControl) { 'F' } else { 'M' }
        & icacls.exe $target /grant:r "*${userSid}:(OI)(CI)${rights}" /T /C /Q | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "icacls reported $LASTEXITCODE item(s) it could not update."
        }
    }
}

if ($WhatIfPreference) {
    Write-Host ''
    Write-Host 'WhatIf: no change was made. Re-run without -WhatIf from an elevated session to apply.' -ForegroundColor Yellow
    return
}

# 5) Verify the result.
Write-Host ''
Write-Host '=== After ===' -ForegroundColor Cyan
Show-IntegrityState $target '  Integrity: '

$remaining = @(Get-IntegrityLabel $target) | Where-Object { $_ -match 'Low' }
if ($remaining) {
    Write-Warning 'The folder still reports a Low integrity label.'
}
else {
    Write-Host '  Integrity: OK (no Low label)' -ForegroundColor Green
}

# Verify with Get-Acl (it resolves account names; icacls prints names too, so matching the
# SID against icacls output would produce a false "user missing" warning).
$folderAcl = Get-Acl -LiteralPath $target -ErrorAction SilentlyContinue
$userAce = $null
if ($folderAcl) {
    $userAce = $folderAcl.Access |
        Where-Object { $_.AccessControlType -eq 'Allow' -and $_.IdentityReference.Value -eq $identity.Name } |
        Select-Object -First 1
}
if ($userAce) {
    Write-Host ("  ACL: OK ({0} = {1})" -f $identity.Name, $userAce.FileSystemRights) -ForegroundColor Green
}
else {
    Write-Warning ("The current user ({0}) is not listed in the folder ACL." -f $identity.Name)
}

$aclText = (& icacls.exe $target 2>&1) -join [Environment]::NewLine
foreach ($pattern in @('\(DENY\)', 'CodexSandbox|SandboxUsers', 'S-1-4-')) {
    if ($aclText -match $pattern) {
        Write-Warning "  ACL still contains a sandbox leftover matching '$pattern'. Run again with -ResetAcl."
        break
    }
}

# Report the executables that matter for launching the app / installing the service.
$executables = @(Get-ChildItem -LiteralPath $target -Filter '*.exe' -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like 'BlockBox*' })
if ($executables.Count -gt 0) {
    Write-Host ''
    Write-Host 'Executables:' -ForegroundColor Cyan
    foreach ($exe in $executables) {
        $labels = @(Get-IntegrityLabel $exe.FullName)
        $state = if ($labels.Count -eq 0) { 'Medium' } else { ($labels -join '; ') }
        Write-Host ("  {0}  ->  {1}" -f $exe.FullName, $state)
    }
}

Write-Host ''
Write-Host 'Next: build and launch the app from your own terminal (not from an AI agent session),' -ForegroundColor Cyan
Write-Host 'otherwise the sandbox can mark this folder Low again.' -ForegroundColor Cyan
Write-Host ''
