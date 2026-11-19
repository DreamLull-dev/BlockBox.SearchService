# 发布到 Microsoft Store

这个目录放的是**商店打包清单模板**和图标。商店版只包含应用本体，不包含全局搜索服务。

## 1. 准备账号

- 注册 [Partner Center](https://partner.microsoft.com/dashboard) 开发者账号（个人/公司，需一次性注册费，以官网为准）；
- 安装 Visual Studio 2022（含「Windows 应用开发」工作负载）和 Windows 10/11 SDK。

## 2. 在 Partner Center 保留应用名称

1. Partner Center → 应用和游戏 → 新建产品 → 保留名称；
2. 进入产品 → 产品标识，记下 **Identity Name**、**Publisher**（形如 `CN=XXXXXXXX-...`）、发布者显示名；
3. 这些值要填进 `Package.appxmanifest`（或直接用 VS 的「Associate with Store」自动填写）。

## 3. 创建打包项目

两种方式，任选其一：

**A. Windows Application Packaging Project（推荐，最稳）**

1. VS 里右键解决方案 → 添加 → 新建项目 → **Windows Application Packaging Project**；
2. 引用 `BlockBox.csproj`；
3. 把本目录的 `Package.appxmanifest` 和 `Assets` 复制到打包项目中，替换占位符：

```
__IDENTITY_NAME__           Partner Center 的 Identity Name
__PUBLISHER__               Partner Center 的 Publisher
__PUBLISHER_DISPLAY_NAME__  发布者显示名
__VERSION__                 1.0.0.0（每次提交递增）
```

4. 右键打包项目 → **Publish → Create App Packages → Microsoft Store**，登录后选择保留的应用名，生成 `.msixupload`。

**B. 单项目 MSIX**

在 `BlockBox.csproj` 里加：

```xml
<EnableMsixTooling>true</EnableMsixTooling>
<WindowsPackageType>MSIX</WindowsPackageType>
```

并放入 `Package.appxmanifest`，然后用 `msbuild /t:Publish` 生成包（VS 的「创建应用包」向导同样适用）。

## 4. 代码里已经处理好的打包差异

| 项目 | 说明 |
|---|---|
| 数据目录 | `AppDataPaths` 检测包身份，使用 `ApplicationData.Current.LocalFolder`（LocalState），不会写到安装目录 |
| 开机启动 | `StartupManager` 在打包环境改用 `StartupTask`（TaskId `BlockBoxStartup`，已在清单里声明） |
| 更新检查 | `AppUpdateService` 在打包环境使用 `StoreContext` 查询商店更新 |
| 全局搜索服务 | **商店版不提供应用内安装**：搜索面板只显示「前往下载」+「重新检测」，用户手动以管理员身份安装服务包（zip/MSIX）。这是为了避免“应用运行时安装包外组件”触发商店审核问题 |

> 自检：`AppUpdateService.IsPackaged`（`Package.Current` 是否可用）决定上述分支，开发时直接运行 exe 会走非打包逻辑。

## 5. 提交到商店

1. Partner Center → 你的产品 → 提交；
2. 上传 `.msixupload`（会自动解析架构、版本）；
3. 填写商店列表：
   - 名称、简短描述、详细描述；
   - 至少 1 张截图（建议 1366×768 或 1920×1080，最多 10 张）；
   - Store 图标（1:1 与 2:3 两种）；
   - 类别、年龄分级问卷；
   - **隐私政策 URL**（应用会读取本地文件名，建议提供）；
   - 支持联系方式；
4. 提交认证（通常 1–3 个工作日）。

## 6. 发布之后

1. 在 Partner Center 产品标识里拿到 **Store ID**；
2. 填入 `BlockBox/Helpers/SupportInfo.cs`：

```csharp
internal const string StoreProductId = "<你的 Store ID>";
```

3. 重新打包发布；应用里的「检查更新 → 打开应用商店」、反馈页的商店入口就会生效；
4. 后续每次发版：递增 `__VERSION__`，重新生成 `.msixupload` 提交即可，商店自动更新用户。

## 7. 容易踩的审核点

- **不要在应用内安装包外组件**（服务安装包）：商店版已改为只提供下载链接；
- 不要写入系统目录或注册表（本应用只写每用户 LocalState 和设置）；
- 隐私政策必须能访问，且说明读取本地文件名、不上传数据；
- 首次提交前用 `Add-AppxPackage` 安装自己生成的 MSIX，确认托盘、开机启动、搜索面板都正常。
