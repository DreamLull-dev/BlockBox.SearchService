using BlockBox.Helpers;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using Windows.Graphics;

namespace BlockBox.Views
{
    public sealed partial class TextInputWindow : Window
    {
        private bool _isSized;
        private readonly Window? _centerOwner;

        public string? Result { get; private set; }

        public TextInputWindow(string title, string initialValue = "", string prompt = "", Window? centerOwner = null)
        {
            InitializeComponent();
            _centerOwner = centerOwner;
            BlockBox.Helpers.WindowAcrylicHelper.Apply(this);
            TitleText.Text = LanguageService.Translate(title);
            PromptText.Text = LanguageService.Translate(prompt);
            InputBox.Text = initialValue;
            InputBox.MaxLength = string.Equals(title, "新建视图", StringComparison.Ordinal) ? 32 : 60;
            InputBox.PlaceholderText = LanguageService.Translate(prompt);
            SaveButton.Content = LanguageService.Translate("保存");
            CancelButton.Content = LanguageService.Translate("取消");
            SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(initialValue);
            InputBox.TextChanged += (_, _) => SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(InputBox.Text);

            ModalWindowHelper.ConfigureBorderlessWindow(this);
            WindowFadeController.Prepare(this);
            RootGrid.Loaded += RootGrid_Loaded;
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
                int width = (int)(420 * scale);
                int height = (int)(230 * scale);
                AppWindow.Resize(new SizeInt32(width, height));
                ModalWindowHelper.CenterOnOwner(_centerOwner ?? App.MainWindow, this);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[文本输入窗口] 尺寸调整失败: {ex}");
            }
            WindowFadeController.FadeAndScaleIn(this, RootGrid);
        }

        private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter && SaveButton.IsEnabled)
            {
                SaveButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string value = InputBox.Text?.Trim() ?? string.Empty;
            if (value.Length == 0) return;
            Result = value;
            WindowFadeController.CloseWithAnimation(this);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
            => WindowFadeController.CloseWithAnimation(this);
    }
}

