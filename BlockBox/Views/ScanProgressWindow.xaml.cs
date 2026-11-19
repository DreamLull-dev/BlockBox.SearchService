using BlockBox.Helpers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace BlockBox.Views
{
    public sealed partial class ScanProgressWindow : Window
    {
        private readonly WindowDragBlocker _dragBlocker;
        private const uint WmCancelMode = 0x001F;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReleaseCapture();

        private bool _isClosed;
        private bool _isSized;
        private bool _closeRequested;
        private DispatcherQueueTimer? _closeTimer;
        private string _status = "正在准备扫描...";

        public ScanProgressWindow()
        {
            InitializeComponent();
            BlockBox.Helpers.WindowAcrylicHelper.Apply(this);
            _dragBlocker = new WindowDragBlocker(this);
            Title = LanguageService.Translate("正在扫描程序");
            StatusText.Text = LanguageService.TranslateProgress(_status);
            LanguageService.LanguageChanged += LanguageService_LanguageChanged;
            ExtendsContentIntoTitleBar = true;
            var titleBar = AppWindow.TitleBar;
            titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(32, 255, 255, 255);
            titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(48, 255, 255, 255);
            WindowFadeController.Prepare(this);
            Closed += (_, _) =>
            {
                _dragBlocker.Dispose();
                _isClosed = true;
                LanguageService.LanguageChanged -= LanguageService_LanguageChanged;
                if (_closeTimer != null)
                {
                    _closeTimer.Stop();
                    _closeTimer.Tick -= CloseTimer_Tick;
                }
                _closeTimer = null;
            };
            RootGrid.Loaded += RootGrid_Loaded;
        }

        public void SetStatus(string message)
        {
            if (_isClosed)
                return;

            if (DispatcherQueue.HasThreadAccess)
            {
                _status = message;
                StatusText.Text = LanguageService.TranslateProgress(message);
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isClosed)
                    SetStatus(message);
            });
        }

        private void LanguageService_LanguageChanged(object? sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isClosed) return;
                Title = LanguageService.Translate("正在扫描程序");
                StatusText.Text = LanguageService.TranslateProgress(_status);
            });
        }

        public void CloseIfOpen()
        {
            if (_isClosed || _closeRequested)
                return;

            _closeRequested = true;
            CancelNativeDrag();
            if (_isClosed)
                return;

            SetStatus("扫描完成，正在关闭...");
            _closeTimer = DispatcherQueue.CreateTimer();
            _closeTimer.Interval = TimeSpan.FromMilliseconds(200);
            _closeTimer.Tick += CloseTimer_Tick;
            _closeTimer.Start();
        }

        private void CloseTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            if (_isClosed)
            {
                sender.Stop();
                return;
            }

            sender.Stop();
            sender.Tick -= CloseTimer_Tick;
            _closeTimer = null;
            CloseWindow();
        }

        private void CancelNativeDrag()
        {
            try
            {
                IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
                SendMessage(windowHandle, WmCancelMode, IntPtr.Zero, IntPtr.Zero);
                ReleaseCapture();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[扫描进度窗体] 取消原生拖动失败: {ex}");
            }
        }

        private void CloseWindow()
        {
            if (_isClosed)
                return;

            _isClosed = true;
            _closeTimer?.Stop();
            _closeTimer = null;
            Close();
        }

        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isSized)
                return;

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
                if (App.MainWindow.Content is FrameworkElement mainContent
                    && mainContent.XamlRoot is { } mainRoot)
                    scale = mainRoot.RasterizationScale;

                int width = (int)(440 * scale);
                int height = (int)(260 * scale);
                AppWindow.Resize(new SizeInt32(width, height));

                bool positionedOverMainWindow = false;
                try
                {
                    var mainWindow = App.MainWindow.AppWindow;
                    var mainSize = mainWindow.Size;
                    if (mainSize.Width > 0 && mainSize.Height > 0)
                    {
                        var mainPosition = mainWindow.Position;
                        AppWindow.Move(new PointInt32(
                            mainPosition.X + (mainSize.Width - width) / 2,
                            mainPosition.Y + (mainSize.Height - height) / 2));
                        positionedOverMainWindow = true;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[扫描进度窗体] 主窗口居中失败: {ex}");
                }

                if (!positionedOverMainWindow)
                {
                    var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
                    if (area != null)
                    {
                        int x = area.WorkArea.X + (area.WorkArea.Width - width) / 2;
                        int y = area.WorkArea.Y + (area.WorkArea.Height - height) / 2;
                        AppWindow.Move(new PointInt32(x, y));
                    }
                }

                WindowFadeController.FadeIn(this);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[扫描进度窗体] 尺寸调整失败: {ex}");
            }
        }
    }
}

