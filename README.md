# Block Box 隐私政策 / Privacy Policy

最后更新：2026-11-20

## 中文

### 1. 概述

Block Box（以下简称“本应用”）是一个本地运行的程序文件管理器，附带可选的全局文件搜索功能。本应用**不收集、不上传任何个人数据**，没有账号系统，也不包含统计分析或遥测组件。

### 2. 本应用不收集的信息

- 不收集姓名、邮箱、电话号码、地址等个人身份信息；
- 不收集设备标识符、广告 ID、位置信息；
- 不收集你的文件内容；
- 不向我们的服务器发送任何数据（我们也没有这样的服务器）。

### 3. 本应用在本地访问和存储的数据

为了提供文件管理功能，本应用会访问你电脑上的以下信息，并且**只保存在本机**：

| 数据 | 用途 | 存放位置 |
|---|---|---|
| 文件名、路径、大小、修改时间、图标 | 显示快捷方式与搜索结果 | 本机数据库与搜索索引 |
| 你创建的分类、标签页、设置 | 应用功能 | `%LOCALAPPDATA%` 或 MSIX 的 LocalState 目录 |
| 全局搜索索引 | 加速文件搜索 | `%ProgramData%\BlockBox\Search`（由可选的搜索服务维护） |

这些数据不会离开你的设备。你可以在应用内删除分类/快捷方式，或直接删除上述目录来清除数据。

### 4. 网络访问

本应用只在以下情况访问网络，且**不会上传文件或个人信息**：

- **检查更新**：Microsoft Store 版本通过 Windows 商店的接口查询更新；非商店版本只显示当前版本并给出商店页面链接；
- **打开外部链接**：当你点击“反馈”“前往下载”等按钮时，会用默认浏览器打开 GitHub 或邮件客户端。打开后由对应网站按其隐私政策处理。

### 5. 可选的“全局搜索服务”

为了支持整盘文件搜索，本应用可以配合一个可选的 Windows 服务（`BlockBoxGlobalSearch`）使用。该服务：

- 以系统账户运行，只读取文件的**名称、大小、修改时间**等元数据，**不读取文件内容**；
- 把索引保存在本机 `%ProgramData%\BlockBox\Search`，不上传；
- 不联网。

该服务需要你手动下载并以管理员身份安装。你可以在“设置 → 应用 → Block Box 搜索服务”中单独卸载它，卸载后索引数据仍保留在本机，可自行删除。

### 6. 权限说明

- 读取文件系统元数据：用于显示和管理文件；
- 开机启动（可选，默认关闭）：由你在设置中开启，使用系统提供的启动项机制；
- 系统托盘图标：用于在关闭主窗口后继续在后台运行。

### 7. 数据保留与删除

- 卸载应用会删除应用自身的数据目录（MSIX 的 LocalState 会随包卸载删除）；
- 搜索索引位于 `%ProgramData%\BlockBox\Search`，卸载搜索服务不会自动删除，你可以手动删除该目录；
- 我们不保留任何服务器端数据，因为没有上传。

### 8. 第三方服务

- **Microsoft Store**：如果你从商店安装，商店会按其隐私声明处理安装与更新信息；
- **GitHub**：下载可选搜索服务、查看反馈时会访问 GitHub，适用 GitHub 的隐私政策。

### 9. 儿童隐私

本应用不面向儿童，也不收集任何年龄或身份信息。

### 10. 政策变更

如果本政策发生变化，我们会更新本页面的“最后更新”日期。重大变更会在应用更新说明中提示。

### 11. 联系方式

如有隐私相关问题，请通过 GitHub Issues 联系我们：
<https://github.com/DreamLull-dev/BlockBox.SearchService/issues>

---

## English

### 1. Overview

Block Box is a local program file manager with an optional global file search feature. The app **does not collect or upload any personal data**. There is no account system and no analytics or telemetry.

### 2. Information we do not collect

- No names, email addresses, phone numbers or postal addresses;
- No device identifiers, advertising IDs or location data;
- No file contents;
- Nothing is sent to any server operated by us (we do not operate one).

### 3. Data accessed and stored locally

To provide file management, the app accesses the following on your device and keeps it **locally only**:

| Data | Purpose | Location |
|---|---|---|
| File names, paths, sizes, timestamps, icons | Show shortcuts and search results | Local database and search index |
| Categories, tabs and settings you create | App functionality | `%LOCALAPPDATA%` or the MSIX LocalState folder |
| Global search index | Faster file search | `%ProgramData%\BlockBox\Search` (maintained by the optional search service) |

This data never leaves your device. You can remove it by deleting categories/shortcuts in the app or by deleting the folders above.

### 4. Network access

The app only uses the network in these cases, and **never uploads files or personal information**:

- **Update check**: the Microsoft Store build queries the Store for updates; other builds only show the current version and link to the Store page;
- **Opening external links**: when you click "Feedback" or "Download", the default browser or mail client opens GitHub. Those sites then handle data under their own privacy policies.

### 5. Optional "global search service"

For whole-disk search, the app can work with an optional Windows service (`BlockBoxGlobalSearch`). The service:

- runs as a system account and reads only file **name, size and modification time** metadata — never file contents;
- stores its index locally in `%ProgramData%\BlockBox\Search` and does not upload it;
- does not access the network.

The service must be downloaded and installed manually with administrator rights. You can uninstall it separately under Settings > Apps > "Block Box Search Service". The index data remains on your device after removal and can be deleted manually.

### 6. Permissions

- Reading file system metadata: to display and manage files;
- Start at login (optional, off by default): enabled by you in Settings through the system startup mechanism;
- System tray icon: to keep running in the background after the main window is closed.

### 7. Data retention and deletion

- Uninstalling the app removes its own data folder (MSIX LocalState is removed with the package);
- The search index lives in `%ProgramData%\BlockBox\Search`; uninstalling the search service does not delete it — you can remove that folder manually;
- We retain no server-side data because nothing is uploaded.

### 8. Third-party services

- **Microsoft Store**: if installed from the Store, the Store handles installation and update information under its own privacy statement;
- **GitHub**: downloading the optional search service or viewing feedback opens GitHub, which is governed by GitHub's privacy policy.

### 9. Children's privacy

The app is not directed at children and does not collect age or identity information.

### 10. Changes

If this policy changes, the "last updated" date on this page will be revised. Significant changes will be noted in the app release notes.

### 11. Contact

For privacy questions, please open an issue at:
<https://github.com/DreamLull-dev/BlockBox.SearchService/issues>
