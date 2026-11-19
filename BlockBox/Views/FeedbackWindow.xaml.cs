using BlockBox.Helpers;
using BlockBox.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.System;

namespace BlockBox.Views
{
    /// <summary>“反馈”窗口：展示作者的联系方式，可一键复制邮箱或直接发邮件。</summary>
    public sealed partial class FeedbackWindow : Window
    {
        private bool _isSized;

        public FeedbackWindow()
        {
            InitializeComponent();
            WindowAcrylicHelper.Apply(this);
            TitleText.Text = LanguageService.Translate("反馈与建议");
            DescriptionText.Text = LanguageService.Translate("遇到问题或有建议，欢迎通过以下方式联系作者。");
            SendMailButton.Content = LanguageService.Translate("发送邮件");
            CopyButton.Content = LanguageService.Translate("复制邮箱");
            CloseButton.Content = LanguageService.Translate("关闭");
            SendMailButton.IsEnabled = SupportInfo.MailtoUri is not null;
            BuildContactRows();

            ModalWindowHelper.ConfigureBorderlessWindow(this);
            RootGrid.Loaded += RootGrid_Loaded;
        }

        private void BuildContactRows()
        {
            ContactPanel.Children.Clear();
            AddTextRow(LanguageService.Translate("邮箱"), SupportInfo.Email);

            if (!string.IsNullOrWhiteSpace(SupportInfo.GitHubUrl))
                AddLinkRow(LanguageService.Translate("项目主页"), SupportInfo.GitHubUrl);

            Uri? storePage = SupportInfo.StorePageUri;
            if (storePage is not null)
                AddLinkRow(LanguageService.Translate("应用商店页面"), storePage.ToString());

            ContactPanel.Children.Add(new TextBlock
            {
                Text = $"Block Box {AppUpdateService.VersionText}",
                FontSize = 12,
                Opacity = 0.55
            });
        }

        private void AddTextRow(string label, string value)
        {
            var panel = new StackPanel { Spacing = 2 };
            panel.Children.Add(new TextBlock { Text = label, FontSize = 12, Opacity = 0.7 });
            panel.Children.Add(new TextBlock
            {
                Text = value,
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap
            });
            ContactPanel.Children.Add(panel);
        }

        private void AddLinkRow(string label, string url)
        {
            var panel = new StackPanel { Spacing = 2 };
            panel.Children.Add(new TextBlock { Text = label, FontSize = 12, Opacity = 0.7 });
            panel.Children.Add(new HyperlinkButton
            {
                Content = url,
                NavigateUri = new Uri(url),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left
            });
            ContactPanel.Children.Add(panel);
        }

        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isSized) return;
            _isSized = true;
            try
            {
                if (AppWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.IsResizable = false;
                    presenter.IsMaximizable = false;
                    presenter.IsMinimizable = false;
                }

                double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
                int width = (int)(480 * scale);
                int height = (int)(Math.Clamp(RootGrid.ActualHeight + 48, 240, 640) * scale);
                AppWindow.Resize(new SizeInt32(width, height));
                ModalWindowHelper.CenterOnOwner(App.MainWindow, this);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[反馈窗口] 尺寸调整失败: {ex}");
            }

            // 只用内容自身的透明度/缩放做入场：之前用 SetLayeredWindowAttributes 对整个 HWND
            // 做逐帧透明（配合亚克力背景）会让鼠标悬停/焦点状态反复重算，看起来一闪一闪。
            WindowFadeController.FadeContentIn(RootGrid);
            Activate();
        }

        private async void SendMail_Click(object sender, RoutedEventArgs e)
        {
            Uri? uri = SupportInfo.MailtoUri;
            if (uri is null) return;
            await Launcher.LaunchUriAsync(uri);
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SupportInfo.Email)) return;
            var package = new DataPackage();
            package.SetText(SupportInfo.Email);
            Clipboard.SetContent(package);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
