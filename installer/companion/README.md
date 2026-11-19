# 构建“Block Box 搜索服务”伴侣 MSIX

这个目录用来把 `BlockBox.SearchService` 打包成一个**独立于主程序**的 MSIX：

- 主程序（包括 Microsoft Store 版）**不含服务**；
- 用户需要整盘搜索时，下载并双击安装这个伴侣包（会弹一次 UAC）；
- 服务由系统随包注册、随包卸载，不会留下 `sc create` 的残留。

## 环境要求

- Windows 10 2004（10.0.19041）及以上；
- .NET 8 SDK（发布服务）；
- Windows SDK 10（`makeappx.exe`、`signtool.exe`，通常在
  `C:\Program Files (x86)\Windows Kits\10\bin\<版本>\x64\`）。

## 构建

```powershell
# 不签名（只用于本地测试；安装前需要先信任证书或开开发者模式）
.\installer\companion\Build-CompanionMsix.ps1 -Version 1.0.0.0 -Publisher "CN=你的证书主题"

# 签名（正式分发）
.\installer\companion\Build-CompanionMsix.ps1 `
    -Version 1.0.0.0 `
    -Publisher "CN=你的证书主题" `
    -CertPath .\certs\BlockBox.pfx `
    -CertPassword "证书密码"

# 证书在 USB 令牌 / 证书存储里（私钥不能导出）时，用指纹签名
.\installer\companion\Build-CompanionMsix.ps1 `
    -Version 1.0.0.0 `
    -Publisher "CN=你的证书主题" `
    -CertThumbprint "证书指纹"
```

产物在 `installer\companion\out\BlockBox.SearchService_<版本>_x64.msix`。

`-Publisher` 必须与签名证书的 Subject 完全一致，否则安装时报
`0x800B0109`（证书不受信任）或 `0x800B010B`（Publisher 不匹配）。

## 零成本分发：zip + 脚本（不需要证书）

没有代码签名证书时，用这个方式发布：

```powershell
.\installer\companion\Build-ServiceZip.ps1 -Version 1.0.0.0
```

产物：`installer\companion\out\BlockBox.SearchService_1.0.0.0_x64.zip`，包含服务程序、
`Install-SearchService.ps1`、`Uninstall-SearchService.ps1` 和一份 `README.txt`。

- 用户把 zip 上传到 GitHub Releases 后，在 Block Box 的搜索面板点「我已下载…」选择该 zip，
  应用会解压到临时目录并提权运行安装脚本（会弹 UAC）；
- 安装脚本会注册「设置 → 应用 → Block Box 搜索服务」的卸载项，用户可以单独卸载；
- 卸载脚本默认保留索引数据，加 `-RemoveData` 可连数据一起删除。

> 注意：`.ps1` 必须是纯 ASCII 或带 UTF-8 BOM。Windows PowerShell 5.1 会把无 BOM 的 UTF-8
> 当作 ANSI 读取，中文会乱码甚至报“字符串缺少终止符”。本仓库的脚本已统一为 ASCII。

## 安装 / 卸载

```powershell
# 安装（会弹 UAC；也可以用资源管理器双击）
Add-AppxPackage .\installer\companion\out\BlockBox.SearchService_1.0.0.0_x64.msix

# 查看
Get-AppxPackage BlockBox.SearchService
Get-Service BlockBoxGlobalSearch

# 卸载（服务会随包一起删除）
Remove-AppxPackage -Package (Get-AppxPackage BlockBox.SearchService).PackageFullName
```

## 测试用自签名证书

```powershell
$cert = New-SelfSignedCertificate -Type CodeSigningCert `
    -Subject "CN=BlockBox" -KeyUsage DigitalSignature `
    -CertStoreLocation Cert:\CurrentUser\My
Export-PfxCertificate -Cert $cert -FilePath .\certs\BlockBox.pfx -Password (ConvertTo-SecureString "test" -AsPlainText -Force)
Export-Certificate -Cert $cert -FilePath .\certs\BlockBox.cer
# 让本机信任（仅测试机）
Import-Certificate -FilePath .\certs\BlockBox.cer -CertStoreLocation Cert:\LocalMachine\Root
```

## 服务端行为说明

- 包内没有安装脚本，服务在首次启动时自行创建 `%ProgramData%\BlockBox\Search`
  并写入索引库/日志（LocalSystem 有权限）；
- 是否限制管道访问：存在 `%ProgramData%\BlockBox\Search\client.sid` 时只放行该用户
  （脚本安装的老路径），不存在时（MSIX 安装）放行本机已认证用户；
- 管道带 `PIPE_REJECT_REMOTE_CLIENTS`，协议只返回文件名，不读文件内容。

## 清单里的受限能力

伴侣包需要三个受限能力（侧载可用，商店需要微软批准且商店安装不提权，所以本包不走商店）：

| 能力 | 用途 |
|---|---|
| `runFullTrust` | `EntryPoint="Windows.FullTrustApplication"` 的桌面进程 |
| `packagedServices` | 声明打包服务扩展 |
| `localSystemServices` | `StartAccount="localSystem"` 的服务账号 |

## 已知限制

- 更新伴侣包时服务会短暂停止；索引库会保留；
- 卸载伴侣包不会删除 `%ProgramData%\BlockBox\Search`（索引数据），需要时可手动清理；
- 伴侣包**不能提交 Microsoft Store**（商店安装不提权，装不了服务）。
