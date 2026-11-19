using BlockBox.Services;
using Microsoft.UI.Xaml;

namespace BlockBox
{
    /// <summary>标题栏“帮助”菜单：反馈、检查更新（与设置页“关于我们”共用同一套流程）。</summary>
    public sealed partial class MainWindow
    {
        private async void MenuFeedback_Click(object sender, RoutedEventArgs e)
            => await UpdatePromptService.ShowFeedbackAsync(this);

        private async void MenuCheckUpdates_Click(object sender, RoutedEventArgs e)
            => await UpdatePromptService.CheckAsync(this);
    }
}
