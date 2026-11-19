using BlockBox.Helpers;
using BlockBox.Services;
using BlockBox.Views;
using Microsoft.UI.Xaml;
using System.Threading.Tasks;

namespace BlockBox
{
    /// <summary>标题栏“帮助”菜单：反馈、检查更新。</summary>
    public sealed partial class MainWindow
    {
        private async void MenuFeedback_Click(object sender, RoutedEventArgs e)
        {
            var window = new FeedbackWindow();
            await ModalWindowHelper.ShowModalAsync(this, window);
        }

        private async void MenuCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            (UpdateCheckStatus status, string? message) = await AppUpdateService.CheckAsync();
            switch (status)
            {
                case UpdateCheckStatus.Available:
                    await InstallUpdateAsync();
                    break;
                case UpdateCheckStatus.UpToDate:
                    await ShowInfoAsync("检查更新",
                        LanguageService.TranslateFormat("当前版本 {version}，已是最新版本。",
                            ("{version}", AppUpdateService.VersionText)));
                    break;
                case UpdateCheckStatus.NotPackaged:
                    await ShowStoreInfoAsync();
                    break;
                default:
                    await ShowInfoAsync("检查更新",
                        LanguageService.TranslateFormat("更新检查失败：{message}", ("{message}", message ?? "")));
                    break;
            }
        }

        private async Task InstallUpdateAsync()
        {
            string version = AppUpdateService.PendingVersionText ?? "";
            var dialog = new ConfirmationWindow("检查更新",
                LanguageService.TranslateFormat("发现新版本（{version}）。要现在更新吗？", ("{version}", version)),
                "现在更新", "稍后");
            await ModalWindowHelper.ShowModalAsync(this, dialog);
            if (dialog.Result != 1) return;

            await ShowInfoAsync("检查更新", LanguageService.Translate("更新已开始，下载完成后会自动安装。"));
            _ = AppUpdateService.InstallAsync();
        }

        private async Task ShowStoreInfoAsync()
        {
            bool hasStore = !string.IsNullOrWhiteSpace(SupportInfo.StoreProductId);
            var dialog = new ConfirmationWindow("检查更新",
                LanguageService.TranslateFormat("当前版本 {version}。商店版本由 Microsoft Store 自动更新。",
                    ("{version}", AppUpdateService.VersionText)),
                hasStore ? "打开应用商店" : "关闭",
                hasStore ? "取消" : null);
            await ModalWindowHelper.ShowModalAsync(this, dialog);
            if (hasStore && dialog.Result == 1)
                await AppUpdateService.OpenStorePageAsync();
        }

        private async Task ShowInfoAsync(string title, string message)
        {
            var dialog = new ConfirmationWindow(title, message, "关闭", null);
            await ModalWindowHelper.ShowModalAsync(this, dialog);
        }
    }
}
