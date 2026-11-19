using BlockBox.Helpers;
using BlockBox.Views;
using Microsoft.UI.Xaml;
using System.Threading.Tasks;

namespace BlockBox.Services
{
    /// <summary>
    /// “检查更新 / 反馈”的交互流程。标题栏的“帮助”菜单和设置页“关于我们”共用同一套弹窗，
    /// 保证两处行为、文案和按钮完全一致。
    /// </summary>
    internal static class UpdatePromptService
    {
        internal static async Task CheckAsync(Window owner)
        {
            (UpdateCheckStatus status, string? message) = await AppUpdateService.CheckAsync();
            switch (status)
            {
                case UpdateCheckStatus.Available:
                    await InstallAsync(owner);
                    break;
                case UpdateCheckStatus.UpToDate:
                    await ShowInfoAsync(owner, "检查更新",
                        LanguageService.TranslateFormat("当前版本 {version}，已是最新版本。",
                            ("{version}", AppUpdateService.VersionText)));
                    break;
                case UpdateCheckStatus.NotPackaged:
                    await ShowStoreInfoAsync(owner);
                    break;
                default:
                    await ShowInfoAsync(owner, "检查更新",
                        LanguageService.TranslateFormat("更新检查失败：{message}", ("{message}", message ?? "")));
                    break;
            }
        }

        private static FeedbackWindow? _feedbackWindow;

        /// <summary>
        /// 反馈窗口用**非模态**方式打开：只建立所有者关系、不禁用主窗体。
        /// 这样它既能保持回收站窗口同款的整窗淡入淡出，又不会因为模态禁用主窗体
        /// 而在淡入期间出现悬停/焦点状态抖动。
        /// </summary>
        internal static Task ShowFeedbackAsync(Window owner)
        {
            if (_feedbackWindow is not null)
            {
                _feedbackWindow.Activate();
                return Task.CompletedTask;
            }

            var window = new FeedbackWindow();
            _feedbackWindow = window;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_feedbackWindow, window)) _feedbackWindow = null;
            };
            ModalWindowHelper.SetOwner(owner, window);
            window.Activate();
            return Task.CompletedTask;
        }

        private static async Task InstallAsync(Window owner)
        {
            string version = AppUpdateService.PendingVersionText ?? "";
            var dialog = new ConfirmationWindow("检查更新",
                LanguageService.TranslateFormat("发现新版本（{version}）。要现在更新吗？", ("{version}", version)),
                "现在更新", "稍后");
            await ModalWindowHelper.ShowModalAsync(owner, dialog);
            if (dialog.Result != 1) return;

            await ShowInfoAsync(owner, "检查更新", LanguageService.Translate("更新已开始，下载完成后会自动安装。"));
            _ = AppUpdateService.InstallAsync();
        }

        private static async Task ShowStoreInfoAsync(Window owner)
        {
            bool hasStore = !string.IsNullOrWhiteSpace(SupportInfo.StoreProductId);
            var dialog = new ConfirmationWindow("检查更新",
                LanguageService.TranslateFormat("当前版本 {version}。商店版本由 Microsoft Store 自动更新。",
                    ("{version}", AppUpdateService.VersionText)),
                hasStore ? "打开应用商店" : "关闭",
                hasStore ? "取消" : null);
            await ModalWindowHelper.ShowModalAsync(owner, dialog);
            if (hasStore && dialog.Result == 1)
                await AppUpdateService.OpenStorePageAsync();
        }

        private static async Task ShowInfoAsync(Window owner, string title, string message)
        {
            var dialog = new ConfirmationWindow(title, message, "关闭", null);
            await ModalWindowHelper.ShowModalAsync(owner, dialog);
        }
    }
}
