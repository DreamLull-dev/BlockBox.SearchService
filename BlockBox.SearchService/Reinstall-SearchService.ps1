<#
.SYNOPSIS
    重新安装 BlockBox 全局搜索服务（停止 → 删除旧服务 → 清理旧程序 → 部署 → 注册 → 启动 → 验证）。

.DESCRIPTION
    由 BlockBox.SearchService 目录下的两个脚本组合而成，因此必须和它们放在同一目录：
      Publish-SearchService.ps1   重新发布（可选，-Publish 时调用）
      Install-SearchService.ps1   部署 + 注册 + 启动 + 存活校验
    索引数据库（ProgramData\BlockBox\Search\search-index.db）和 client.sid 默认保留，
    只有显式加 -ResetIndex 才会删除索引、下次启动全量重建。

.PARAMETER Publish
    先执行 Publish-SearchService.ps1（Release、win-x64、自包含），再安装新产物。

.PARAMETER ServiceExecutable
    指定发布目录里的 BlockBox.SearchService.exe；默认 publish\win-x64\BlockBox.SearchService.exe。

.PARAMETER ResetIndex
    同时删除索引库，服务下次启动会重新扫描 MFT 全量建索引（可能要几分钟到十几分钟）。

.PARAMETER NoStart
    只安装、不启动服务。

.EXAMPLE
    .\Reinstall-SearchService.ps1 -Publish

.EXAMPLE
    .\Reinstall-SearchService.ps1

.EXAMPLE
    .\Reinstall-SearchService.ps1 -Publish -ResetIndex
#>
[CmdletBinding()]
param(
    [string] $ServiceExecutable,
    [switch] $Publish,
    [switch] $ResetIndex,
    [switch] $NoStart
)

$ErrorActionPreference = 'Stop'

$serviceName = 'BlockBoxGlobalSearch'
$pipeName = 'BlockBox.GlobalSearch.v1'
$installDirectory = Join-Path $env:ProgramFiles 'BlockBox\SearchService'
$installedExecutable = Join-Path $installDirectory 'BlockBox.SearchService.exe'
$dataDirectory = Join-Path $env:ProgramData 'BlockBox\Search'
$serviceLogPath = Join-Path $dataDirectory 'service.log'
$defaultExecutable = Join-Path $PSScriptRoot 'publish\win-x64\BlockBox.SearchService.exe'

function Get-LogTail([int] $lines = 15) {
    if (-not (Test-Path -LiteralPath $serviceLogPath)) { return '(服务日志尚未生成)' }
    return ((Get-Content -LiteralPath $serviceLogPath -Tail $lines) -join [Environment]::NewLine)
}

# 0) 必须从管理员 PowerShell 运行：注册/删除服务、写 Program Files 都需要提权。
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '请在“以管理员身份运行”的 PowerShell 中执行此脚本。'
}

# 1) 可选：重新发布
if ($Publish) {
    Write-Host '== 1/5 重新发布服务 ==' -ForegroundColor Cyan
    $publishScript = Join-Path $PSScriptRoot 'Publish-SearchService.ps1'
    if (-not (Test-Path -LiteralPath $publishScript)) { throw "找不到发布脚本：$publishScript" }
    & $publishScript
    if ($LASTEXITCODE -ne 0) { throw '服务发布失败。' }
}

if (-not $ServiceExecutable) { $ServiceExecutable = $defaultExecutable }
if (-not (Test-Path -LiteralPath $ServiceExecutable -PathType Leaf)) {
    throw "找不到服务可执行文件：$ServiceExecutable`n请先运行 Publish-SearchService.ps1，或给本脚本加 -Publish。"
}
$sourceDirectory = Split-Path -Parent (Resolve-Path -LiteralPath $ServiceExecutable).Path
foreach ($required in @('BlockBox.SearchService.dll', 'Microsoft.Data.Sqlite.dll', 'e_sqlite3.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $sourceDirectory $required))) {
        throw "发布目录不完整，缺少 $required：$sourceDirectory"
    }
}

# 2) 停止并删除旧服务
Write-Host '== 2/5 停止并删除旧服务 ==' -ForegroundColor Cyan
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -ne $service) {
    $service.Refresh()
    if ($service.Status -ne 'Stopped' -and $service.Status -ne 'StopPending') {
        try { Stop-Service -Name $serviceName -Force -ErrorAction Stop } catch { }
    }
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $service.Refresh()
        if ($service.Status -eq 'Stopped') { break }
    }
    if ($service.Status -ne 'Stopped') {
        # 服务卡住时只终止服务注册指向的那个进程，不按进程名乱杀。
        $state = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
        $configuredPath = if ($state) { ([string]$state.PathName).Trim('"') } else { '' }
        if ($state -and $state.ProcessId -gt 0 -and
            [string]::Equals($configuredPath, $installedExecutable, [StringComparison]::OrdinalIgnoreCase)) {
            Stop-Process -Id $state.ProcessId -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 2
        }
        else {
            throw '服务在 20 秒内没有停止，且无法确认其进程路径，已放弃强制结束。'
        }
    }
    & sc.exe delete $serviceName | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Warning "sc.exe delete 返回 $LASTEXITCODE，继续尝试安装。" }
    Start-Sleep -Seconds 2
}

# 3) 清理旧的程序文件（只动安装目录，不动 ProgramData 里的索引和 client.sid）
Write-Host '== 3/5 清理旧程序文件 ==' -ForegroundColor Cyan
foreach ($process in @(Get-Process -Name 'BlockBox.SearchService' -ErrorAction SilentlyContinue)) {
    $path = $null
    try { $path = $process.Path } catch { }
    if ($path -and [string]::Equals($path, $installedExecutable, [StringComparison]::OrdinalIgnoreCase)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
}
if (Test-Path -LiteralPath $installDirectory) {
    Remove-Item -LiteralPath $installDirectory -Recurse -Force
}

# 4) 部署 + 注册 + 启动（交给已验证过的安装脚本）
Write-Host '== 4/5 部署、注册并启动服务 ==' -ForegroundColor Cyan
$installScript = Join-Path $PSScriptRoot 'Install-SearchService.ps1'
if (-not (Test-Path -LiteralPath $installScript)) { throw "找不到安装脚本：$installScript" }
$installArguments = @{ ServiceExecutable = $ServiceExecutable }
if ($ResetIndex) { $installArguments['ResetIndex'] = $true }
if ($NoStart) { $installArguments['NoStart'] = $true }
& $installScript @installArguments

# 5) 结果确认：服务状态、命名管道、索引库、日志尾部
if ($NoStart) {
    Write-Host '== 5/5 已安装，未启动（-NoStart） ==' -ForegroundColor Yellow
    return
}

Write-Host '== 5/5 验证 ==' -ForegroundColor Cyan
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -eq $service -or $service.Status -ne 'Running') {
    throw "服务未处于 Running 状态。`n$(Get-LogTail)"
}

$pipeReady = $false
for ($attempt = 0; $attempt -lt 20 -and -not $pipeReady; $attempt++) {
    Start-Sleep -Milliseconds 500
    try {
        $pipeReady = @(Get-ChildItem -Path '\\.\pipe\' -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -eq $pipeName }).Count -gt 0
    }
    catch {
        # 管道枚举失败不影响安装结论，下面按“未知”提示。
        break
    }
}

$indexPath = Join-Path $dataDirectory 'search-index.db'
$indexSize = if (Test-Path -LiteralPath $indexPath) {
    '{0:N1} MB' -f ((Get-Item -LiteralPath $indexPath).Length / 1MB)
} else {
    '不存在（服务启动后会新建并开始建索引）'
}

Write-Host ''
Write-Host "服务名    : $serviceName"
Write-Host "状态      : $($service.Status)"
Write-Host "安装目录  : $installDirectory"
Write-Host "数据目录  : $dataDirectory"
Write-Host "索引库    : $indexPath（$indexSize）"
Write-Host "命名管道  : $(if ($pipeReady) { "已创建（$pipeName）" } else { "暂未检测到，稍后可用 Get-Service $serviceName 再确认" })"
Write-Host ''
Write-Host '最近服务日志：'
Get-LogTail 10 | ForEach-Object { Write-Host "  $_" }
Write-Host ''
Write-Host '完成。重启 BlockBox 主程序后即可使用新的搜索服务。' -ForegroundColor Green
