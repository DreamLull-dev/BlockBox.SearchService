using BlockBox.Helpers;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;
using Windows.Graphics;

namespace BlockBox.Views
{
    public sealed partial class StartupWindow : Window
    {
        private readonly WindowDragBlocker _dragBlocker;
        private bool _sized;
        private bool _isClosed;
        private string _status = "正在加载...";

        public StartupWindow()
        {
            InitializeComponent();
            BlockBox.Helpers.WindowAcrylicHelper.Apply(this);
            _dragBlocker = new WindowDragBlocker(this);
            Closed += (_, _) =>
            {
                _isClosed = true;
                LanguageService.LanguageChanged -= LanguageService_LanguageChanged;
                _dragBlocker.Dispose();
            };
            LanguageService.LanguageChanged += LanguageService_LanguageChanged;
            Title = "BlockBox";
            LanguageService.Apply(RootGrid, LanguageService.CurrentLanguageCode);
            SetStatus(_status);

            // 将 XAML 材质延伸到标题栏下面，避免标题栏单独透出桌面壁纸。
            ExtendsContentIntoTitleBar = true;
            var titleBar = AppWindow.TitleBar;
            titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(32, 255, 255, 255);
            titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(48, 255, 255, 255);

            WindowFadeController.Prepare(this);
            RootGrid.Loaded += RootGrid_Loaded;
        }

        private void LanguageService_LanguageChanged(object? sender, EventArgs e)
        {
            if (_isClosed) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isClosed) StatusText.Text = LanguageService.TranslateProgress(_status);
            });
        }

        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (_sized) return;
            _sized = true;

            try
            {
                if (AppWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.IsResizable = false;
                    presenter.IsMaximizable = false;
                    presenter.IsMinimizable = false;
                }

                double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
                int width = (int)(360 * scale);
                int height = (int)(420 * scale);
                AppWindow.Resize(new SizeInt32(width, height));

                var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
                if (area != null)
                {
                    int x = area.WorkArea.X + (area.WorkArea.Width - width) / 2;
                    int y = area.WorkArea.Y + (area.WorkArea.Height - height) / 2;
                    AppWindow.Move(new PointInt32(x, y));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Splash] 尺寸调整失败: {ex}");
            }

            // 尺寸确定后再对整个原生窗体（含系统背景和全部 XAML 内容）淡入。
            WindowFadeController.FadeIn(this);
        }

        public void SetStatus(string message)
        {
            if (_isClosed) return;
            _status = message;
            try
            {
                StatusText.Text = LanguageService.TranslateProgress(message);
            }
            catch (Exception ex)
            {
                // 翻译/赋值失败不能把启动流程带崩，也不能让状态悄悄停住。
                StartupLog.Error("SetStatus(" + message + ")", ex);
            }
        }

        public async Task FadeOutAsync()
        {
            await WindowFadeController.FadeOutAsync(this);
            Close();
        }
    }
}

