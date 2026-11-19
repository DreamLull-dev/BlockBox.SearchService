using BlockBox.Helpers;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Graphics;

namespace BlockBox.Views
{
    public sealed partial class ConfirmationWindow : Window
    {
        private bool _isSized;
        private readonly Window? _centerOwner;

        public int Result { get; private set; }

        public ConfirmationWindow(string title, object content, string primaryText, string cancelText,
            string? secondaryText = null, int defaultResult = 0, Window? centerOwner = null)
        {
            InitializeComponent();
            _centerOwner = centerOwner;
            BlockBox.Helpers.WindowAcrylicHelper.Apply(this);
            TitleText.Text = LanguageService.Translate(title);
            MessagePresenter.Content = content is string text
                ? new TextBlock { Text = LanguageService.Translate(text), TextWrapping = TextWrapping.Wrap }
                : content;
            PrimaryButton.Content = LanguageService.Translate(primaryText);
            CancelButton.Content = LanguageService.Translate(cancelText);
            if (!string.IsNullOrWhiteSpace(secondaryText))
            {
                SecondaryButton.Content = LanguageService.Translate(secondaryText);
                SecondaryButton.Visibility = Visibility.Visible;
            }
            Result = defaultResult;

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
                int width = (int)(520 * scale);
                int height = (int)(Math.Clamp(RootGrid.ActualHeight + 48, 220, 560) * scale);
                AppWindow.Resize(new SizeInt32(width, height));
                ModalWindowHelper.CenterOnOwner(_centerOwner ?? App.MainWindow, this);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[确认窗口] 尺寸调整失败: {ex}");
            }
            WindowFadeController.FadeAndScaleIn(this, RootGrid);
        }

        private void PrimaryButton_Click(object sender, RoutedEventArgs e)
        {
            Result = 1;
            WindowFadeController.CloseWithAnimation(this);
        }

        private void SecondaryButton_Click(object sender, RoutedEventArgs e)
        {
            Result = 2;
            WindowFadeController.CloseWithAnimation(this);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Result = 0;
            WindowFadeController.CloseWithAnimation(this);
        }
    }
}

