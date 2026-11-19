using BlockBox.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Services.Store;
using Windows.System;

namespace BlockBox.Services
{
    internal enum UpdateCheckStatus
    {
        /// <summary>已经是最新版本。</summary>
        UpToDate,
        /// <summary>商店里有新版本可用。</summary>
        Available,
        /// <summary>当前是未打包（开发/绿色版），没有商店更新通道。</summary>
        NotPackaged,
        /// <summary>检查失败（网络、商店服务异常等）。</summary>
        Failed
    }

    /// <summary>
    /// 更新检查。
    /// <para>
    /// 应用发布到 Microsoft Store 后，更新由商店自动完成；应用内的“检查更新”用的是官方
    /// StoreContext API（仅对带商店身份的 MSIX 包有效）。未打包时没有可用的更新通道，
    /// 界面会提示版本号并给出商店入口。
    /// </para>
    /// </summary>
    internal static class AppUpdateService
    {
        private static IReadOnlyList<StorePackageUpdate>? _pendingUpdates;

        /// <summary>当前是否是带应用商店身份的打包版本。</summary>
        internal static bool IsPackaged { get; } = DetectPackaged();

        internal static string VersionText { get; } = DetectVersion();

        /// <summary>待安装版本号，例如 1.2.0。</summary>
        internal static string? PendingVersionText { get; private set; }

        internal static async Task<(UpdateCheckStatus Status, string? Message)> CheckAsync()
        {
            _pendingUpdates = null;
            PendingVersionText = null;
            if (!IsPackaged) return (UpdateCheckStatus.NotPackaged, null);

            try
            {
                var context = StoreContext.GetDefault();
                IReadOnlyList<StorePackageUpdate> updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();
                _pendingUpdates = updates;
                if (updates.Count == 0) return (UpdateCheckStatus.UpToDate, null);

                PackageVersion version = updates[0].Package.Id.Version;
                PendingVersionText = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
                return (UpdateCheckStatus.Available, null);
            }
            catch (Exception ex)
            {
                return (UpdateCheckStatus.Failed, ex.Message);
            }
        }

        /// <summary>开始下载并安装已发现的更新（后台进行，完成后由商店接管安装）。</summary>
        internal static async Task<bool> InstallAsync()
        {
            if (_pendingUpdates is null || _pendingUpdates.Count == 0) return false;
            try
            {
                var context = StoreContext.GetDefault();
                StorePackageUpdateResult result =
                    await context.RequestDownloadAndInstallStorePackageUpdatesAsync(_pendingUpdates);
                return result.OverallState == StorePackageUpdateState.Completed;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static async Task<bool> OpenStorePageAsync()
        {
            Uri? uri = SupportInfo.StorePageUri;
            if (uri is null) return false;
            return await Launcher.LaunchUriAsync(uri);
        }

        internal static async Task<bool> OpenStoreReviewAsync()
        {
            Uri? uri = SupportInfo.StoreReviewUri;
            if (uri is null) return false;
            return await Launcher.LaunchUriAsync(uri);
        }

        private static bool DetectPackaged()
        {
            try
            {
                _ = Package.Current.Id;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string DetectVersion()
        {
            try
            {
                PackageVersion version = Package.Current.Id.Version;
                return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
            }
            catch
            {
                Version? version = typeof(AppUpdateService).Assembly.GetName().Version;
                return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
            }
        }
    }
}
