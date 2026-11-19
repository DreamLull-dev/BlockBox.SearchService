using BlockBox.Data;
using BlockBox.Helpers;
using BlockBox.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BlockBox.Services
{
    public static class VerifyService
    {
        /// <summary>
        /// 全量校验所有 Shortcut。返回"源文件丢失"的列表。
        /// 在后台线程跑，低优先级，慢慢来。
        /// </summary>
        public static async Task<List<ShortcutItem>> VerifyAllAsync(
            CancellationToken ct = default)
        {
            var missing = new List<ShortcutItem>();

            await Task.Run(async () =>
            {
                // 降到低优先级，别抢 UI
                try { Thread.CurrentThread.Priority = ThreadPriority.BelowNormal; }
                catch { }

                var all = ShortcutRepository.GetAll();
                System.Diagnostics.Debug.WriteLine(
                    $"[校验] 开始; 记录数={all.Count}; User={Environment.UserDomainName}\\{Environment.UserName}; Base={AppContext.BaseDirectory}; CurrentDirectory={Environment.CurrentDirectory}; Is64Bit={Environment.Is64BitProcess}");

                foreach (var s in all)
                {// ⭐ UWP 路径不做文件检查
                    if (!string.IsNullOrEmpty(s.TargetPath)
                        && s.TargetPath.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[校验] 跳过 UWP 目标; Name={s.Name}; Target={s.TargetPath}");
                        continue;
                    }
                    if (ct.IsCancellationRequested) return;

                    try
                    {
                        VerifyOne(s, missing);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[校验] 单条校验异常; Name={s.Name}; Id={s.Id}; Target={s.TargetPath}; IsFile={s.IsFile}; IsShared={s.IsShared}; BackedUp={s.IsBackedUp}; Exception={ex}");
                    }

                    // 每处理完一个歇 30ms，别一直占 CPU
                    // Do not pass the token to Task.Delay: cancellation of a
                    // delay throws TaskCanceledException, even though shutdown
                    // is an expected condition here.
                    await Task.Delay(30);
                    if (ct.IsCancellationRequested)
                        return;
                }
            });

            System.Diagnostics.Debug.WriteLine(
                $"[校验] 扫描完成; 丢失数={missing.Count}; Missing={string.Join(" | ", missing.Select(s => $"{s.Name} [{s.Id}] => {s.TargetPath}"))}");

            return missing;
        }

        public static int RepairSharedApplicationTargetPaths()
        {
            int repaired = 0;
            foreach (var shortcut in ShortcutRepository.GetAll())
            {
                if (shortcut.IsShared && !shortcut.IsFile
                    && TryRepairSharedApplicationTargetPath(shortcut))
                {
                    repaired++;
                }
            }

            return repaired;
        }

        /// <summary>
        /// 只校验"指定的一批"（用于关闭时只校验本次打开过的）
        /// </summary>
        public static async Task<List<ShortcutItem>> VerifyListAsync(
            List<ShortcutItem> targets,
            CancellationToken ct = default)
        {
            var missing = new List<ShortcutItem>();

            await Task.Run(async () =>
            {
                foreach (var s in targets)
                {
                    if (ct.IsCancellationRequested) return;
                    try { VerifyOne(s, missing); }
                    catch { }
                    await Task.Delay(20);
                    if (ct.IsCancellationRequested)
                        return;
                }
            });

            return missing;
        }

        // ==================== 单条校验 ====================

        private static void VerifyOne(ShortcutItem s, List<ShortcutItem> missing)
        {
            if (!string.IsNullOrWhiteSpace(s.TargetPath)
                && File.Exists(s.TargetPath))
            {
                TryRepairMissingVersionedApplicationTargetPath(s);
            }

            if (IsShortcutFilePath(s.TargetPath))
            {
                if (!TryCheckFileExists(s.TargetPath, out bool shortcutExists))
                    return;

                if (shortcutExists)
                {
                    if (s.SourceState == SourceState.Missing)
                    {
                        s.SourceState = SourceState.Valid;
                        ShortcutRepository.UpdateSourceState(s.Id, SourceState.Valid, s.SourceMd5, DateTime.Now);
                    }
                    System.Diagnostics.Debug.WriteLine(
                        $"[校验] 快捷方式文件可用; Name={s.Name}; Target={s.TargetPath}");
                    return;
                }
            }

            // 情况 1：应用程序 / 快捷方式（不备份）
            if (!s.IsBackedUp || string.IsNullOrEmpty(s.BackupPath))
            {
                if (!TryCheckFileExists(s.TargetPath, out bool srcExists))
                    return;

                if (!srcExists)
                {
                    srcExists = TryRepairMissingVersionedApplicationTargetPath(s);
                    if (!srcExists && s.IsShared && !s.IsFile)
                        srcExists = TryRepairSharedApplicationTargetPath(s);
                }

                if (!srcExists)
                {
                    if (s.SourceState != SourceState.Missing)
                    {
                        s.SourceState = SourceState.Missing;
                        ShortcutRepository.UpdateSourceState(
                            s.Id, SourceState.Missing, s.SourceMd5, DateTime.Now);
                    }
                    missing.Add(s);
                }
                else if (s.SourceState == SourceState.Missing)
                {
                    s.SourceState = SourceState.Valid;
                    ShortcutRepository.UpdateSourceState(
                        s.Id, SourceState.Valid, s.SourceMd5, DateTime.Now);
                }
                return;
            }

            // 情况 2：普通文件（有备份）
            string backupAbs = BackupPathHelper.MakeAbsolute(s.BackupPath);
            bool backupExists = File.Exists(backupAbs);
            bool srcExists2 = !string.IsNullOrEmpty(s.TargetPath)
                           && File.Exists(s.TargetPath);

            // 2.0 源文件丢了 + 备份也丢了 → 彻底无救
            if (!srcExists2 && !backupExists)
            {
                if (s.SourceState != SourceState.Missing)
                {
                    s.SourceState = SourceState.Missing;
                    ShortcutRepository.UpdateSourceState(
                        s.Id, SourceState.Missing, s.SourceMd5, DateTime.Now);
                }
                missing.Add(s);          // 加进"丢失"列表，弹窗时告诉用户两条都没了
                return;
            }

            // 2.1 源文件丢失（备份还在）
            if (!srcExists2)
            {
                if (s.SourceState != SourceState.Missing)
                {
                    s.SourceState = SourceState.Missing;
                    ShortcutRepository.UpdateSourceState(
                        s.Id, SourceState.Missing, s.SourceMd5, DateTime.Now);
                }
                missing.Add(s);
                return;
            }

            // 2.2 源文件存在，但备份文件被用户删了 → 重建
            if (!backupExists)
            {
                try
                {
                    string backupDir = Path.GetDirectoryName(backupAbs) ?? "";
                    BackupPathHelper.EnsureDirectory(backupDir);
                    File.Copy(s.TargetPath, backupAbs, overwrite: false);

                    System.Diagnostics.Debug.WriteLine(
                        $"[校验] {s.Name} 备份丢失，已重建 → {s.BackupPath}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[校验] 重建备份失败: {ex.Message}");
                    return;
                }
            }

            // 2.3 源文件存在，算 MD5
            string currentMd5;
            try
            {
                currentMd5 = Md5Helper.Compute(s.TargetPath);
            }
            catch
            {
                return;
            }

            if (currentMd5 == s.SourceMd5)
            {
                if (s.SourceState != SourceState.Valid)
                {
                    s.SourceState = SourceState.Valid;
                    ShortcutRepository.UpdateSourceState(
                        s.Id, SourceState.Valid, s.SourceMd5, DateTime.Now);
                }
                return;
            }

            // 2.4 源文件改了 → 覆盖备份
            try { File.Copy(s.TargetPath, backupAbs, overwrite: true); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[校验] 覆盖备份失败: {ex.Message}");
                return;
            }

            s.SourceMd5 = currentMd5;
            s.SourceState = SourceState.Valid;
            ShortcutRepository.UpdateSourceState(
                s.Id, SourceState.Valid, currentMd5, DateTime.Now);

            System.Diagnostics.Debug.WriteLine($"[校验] {s.Name} 已更新备份");
        }

        private static bool TryRepairMissingVersionedApplicationTargetPath(ShortcutItem shortcut)
        {
            string targetPath = shortcut.TargetPath ?? string.Empty;
            string executableName = Path.GetFileName(targetPath);
            if (!string.Equals(Path.GetExtension(executableName), ".exe",
                    StringComparison.OrdinalIgnoreCase))
                return false;

            string? parentDirectory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(parentDirectory))
                return false;

            bool isVersionDirectory = Version.TryParse(
                Path.GetFileName(parentDirectory), out _);
            string? applicationDirectory;
            if (string.Equals(Path.GetFileName(parentDirectory), "Application",
                    StringComparison.OrdinalIgnoreCase))
            {
                applicationDirectory = parentDirectory;
                isVersionDirectory = false;
            }
            else
            {
                if (!isVersionDirectory)
                    return false;

                // 支持 Application、OneDrive 等不同的版本化安装目录：
                // <应用目录>\\<版本号>\\<程序.exe>
                applicationDirectory = Path.GetDirectoryName(parentDirectory);
            }

            if (!Directory.Exists(applicationDirectory))
                return false;

            try
            {
                string? latestTarget = Directory.GetDirectories(applicationDirectory)
                    .Select(directory => new
                    {
                        Directory = directory,
                        Version = Version.TryParse(
                            Path.GetFileName(directory), out Version? version)
                            ? version
                            : null
                    })
                    .Where(item => item.Version != null
                        && File.Exists(Path.Combine(item.Directory, executableName)))
                    .OrderByDescending(item => item.Version)
                    .Select(item => Path.Combine(item.Directory, executableName))
                    .FirstOrDefault();

                if (string.IsNullOrWhiteSpace(latestTarget)
                    || string.Equals(latestTarget, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (!isVersionDirectory)
                        return false;

                    int removed = ShortcutRepository.RemoveByTargetPath(targetPath);
                    if (removed > 0)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[校验] 未找到 {executableName} 新版本，已清理失效记录; Target={targetPath}; Count={removed}");
                        return true;
                    }

                    return false;
                }

                if (!ShortcutRepository.UpdateTargetPath(
                        shortcut.Id, latestTarget, shortcut.IsShared))
                    return false;

                shortcut.TargetPath = latestTarget;
                System.Diagnostics.Debug.WriteLine(
                    $"[校验] 版本化应用目标已更新; Name={shortcut.Name}; Target={latestTarget}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[校验] 扫描版本化应用新版本失败; Target={targetPath}; Error={ex.Message}");
                return false;
            }
        }

        private static bool TryCheckFileExists(string path, out bool exists)
        {
            exists = false;
            if (string.IsNullOrWhiteSpace(path))
            {
                System.Diagnostics.Debug.WriteLine("[校验] 空目标路径; 按不存在处理");
                return true;
            }

            string? root = null;
            try
            {
                root = Path.GetPathRoot(path);
                bool driveReady = true;
                if (!string.IsNullOrWhiteSpace(root))
                {
                    var drive = new DriveInfo(root);
                    driveReady = drive.IsReady;
                    if (!drive.IsReady)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[校验] 目标盘不可用，暂不判定为丢失; Target={path}; Root={root}; DriveReady={driveReady}; User={Environment.UserDomainName}\\{Environment.UserName}; Base={AppContext.BaseDirectory}");
                        return false;
                    }
                }

                string fullPath = Path.GetFullPath(path);
                // File.Exists 对文件或父目录不存在的情况直接返回 false，
                // 避免将正常的“目标已被卸载/升级”变成首次机会异常。
                exists = File.Exists(path);
                System.Diagnostics.Debug.WriteLine(
                    $"[校验] 目标探测; Target={path}; FullPath={fullPath}; Root={root}; DriveReady={driveReady}; Exists={exists}; User={Environment.UserDomainName}\\{Environment.UserName}; Base={AppContext.BaseDirectory}");
                return true;
            }
            catch (FileNotFoundException ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[校验] 明确文件不存在; Target={path}; Root={root}; Error={ex.Message}; User={Environment.UserDomainName}\\{Environment.UserName}; Base={AppContext.BaseDirectory}");
                return true;
            }
            catch (DirectoryNotFoundException ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[校验] 路径中的目录不存在; Target={path}; Root={root}; Error={ex.Message}; User={Environment.UserDomainName}\\{Environment.UserName}; Base={AppContext.BaseDirectory}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[校验] 无法确认目标文件状态，暂不判定为丢失; Target={path}; Root={root}; Exception={ex}; User={Environment.UserDomainName}\\{Environment.UserName}; Base={AppContext.BaseDirectory}; CurrentDirectory={Environment.CurrentDirectory}");
                return false;
            }
        }

        private static bool IsShortcutFilePath(string? path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                    || Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryResolveProgramsPath(string targetPath, out string? correctedPath)
        {
            correctedPath = null;
            if (string.IsNullOrWhiteSpace(targetPath))
                return false;

            string normalizedPath = targetPath.Replace('/', '\\');
            const string marker = "Programs\\";
            int markerIndex = normalizedPath.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
                return false;

            string relativePath = normalizedPath[(markerIndex + marker.Length)..];
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var userNames = new[]
            {
                Environment.UserName,
                Path.GetFileName(userProfile.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            }.Where(name => !string.IsNullOrWhiteSpace(name))
             .Distinct(StringComparer.OrdinalIgnoreCase)
             .ToList();

            var driveRoots = Environment.GetLogicalDrives().ToList();
            string? targetDrive = Path.GetPathRoot(normalizedPath);
            if (!string.IsNullOrWhiteSpace(targetDrive)
                && !driveRoots.Contains(targetDrive, StringComparer.OrdinalIgnoreCase))
            {
                driveRoots.Insert(0, targetDrive);
            }

            var roots = driveRoots
                .SelectMany(drive => userNames.Select(userName =>
                    Path.Combine(drive, "Users", userName, "AppData", "Local", "Programs")))
                .Append(Path.Combine(localAppData, "Programs"))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (string root in roots)
            {
                string candidate;
                try
                {
                    string fullRoot = Path.GetFullPath(root);
                    candidate = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
                    string relativeCandidate = Path.GetRelativePath(fullRoot, candidate);
                    if (relativeCandidate == ".."
                        || relativeCandidate.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                        || Path.IsPathRooted(relativeCandidate))
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                if (!File.Exists(candidate))
                    continue;

                correctedPath = candidate;
                return true;
            }

            return false;
        }

        private static bool TryRepairSharedApplicationTargetPath(ShortcutItem shortcut)
        {
            if (!TryResolveProgramsPath(shortcut.TargetPath, out string? correctedPath))
            {
                return false;
            }

            if (!ShortcutRepository.UpdateSharedTargetPath(shortcut.Id, correctedPath))
            {
                bool merged = ShortcutRepository.MergeSharedShortcutIntoExisting(shortcut.Id, correctedPath);
                if (!merged)
                    return false;

                shortcut.TargetPath = correctedPath!;
                System.Diagnostics.Debug.WriteLine(
                    $"[校验] 已合并并移除损坏的共享程序重复记录 {shortcut.Name}: {correctedPath}");
                return true;
            }

            shortcut.TargetPath = correctedPath!;
            if (shortcut.SourceState == SourceState.Missing)
            {
                shortcut.SourceState = SourceState.Valid;
                ShortcutRepository.UpdateSourceState(
                    shortcut.Id, SourceState.Valid, shortcut.SourceMd5, DateTime.Now);
            }

            System.Diagnostics.Debug.WriteLine(
                $"[校验] 已修复共享程序路径 {shortcut.Name}: {correctedPath}");
            return true;
        }
    }
}
