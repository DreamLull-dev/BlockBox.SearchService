# Block Box 桌面安装包

这里是 **Inno Setup** 模板：一次 UAC 安装“应用 + 全局搜索服务”，卸载时自动删除服务。

> Microsoft Store 的 MSIX 版本无法在卸载时执行脚本。商店版用户请到
> “设置 → 应用 → 已安装的应用”里单独卸载“Block Box 搜索服务”（安装脚本会注册这个卸载项）。

## 准备发布目录

```powershell
# 应用（自包含，用户不用装 .NET / Windows App SDK 运行时）
dotnet publish BlockBox\BlockBox.csproj -c Release -r win-x64 --self-contained true -o publish\app

# 搜索服务（自包含）
.\BlockBox.SearchService\Publish-SearchService.ps1
```

## 编译安装包

1. 安装 [Inno Setup 6](https://jrsoftware.org/isdl.php)；
2. 用 Inno Setup Compiler 打开 [BlockBox.iss](BlockBox.iss)，或命令行：

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" .\installer\desktop\BlockBox.iss
```

产物在 `dist\BlockBox-Setup-<版本>.exe`。

## 安装 / 卸载行为

| 操作 | 行为 |
|---|---|
| 安装 | 复制应用和服务 → 执行 `Install-SearchService.ps1`（注册服务、写 `client.sid`、启动并验证）→ 注册卸载项 |
| 升级 | 重新运行安装包：脚本会先停旧服务、删注册，再部署新文件（索引库保留） |
| 卸载应用 | `[UninstallRun]` 自动执行 `Uninstall-SearchService.ps1`，停服务、删注册、清安装目录；索引数据默认保留 |
| 单独卸载服务 | 设置 → 应用 → “Block Box 搜索服务” |

卸载时保留索引数据（`%ProgramData%\BlockBox\Search`）。要连数据一起删，可以手动删除该目录，或运行
`Uninstall-SearchService.ps1 -RemoveData`。
