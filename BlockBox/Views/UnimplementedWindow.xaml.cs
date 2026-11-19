using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using BlockBox.Helpers;
using System;
using System.Threading.Tasks;
using Windows.Graphics;

namespace BlockBox.Views
{
    public sealed partial class UnimplementedWindow : Window
    {
        private bool _closing;

        public UnimplementedWindow()
        {
            InitializeComponent();
            BlockBox.Helpers.WindowAcrylicHelper.Apply(this);
            ModalWindowHelper.ConfigureBorderlessWindow(this);
            SetTitleBar(TitleBarArea);
            Root.Loaded += async (_, _) => await PlayAnimationAsync();
            AppWindow.Resize(new SizeInt32(520, 340));
            AppWindow.Title = " ";
        }

        private async Task PlayAnimationAsync()
        {
            try
            {
                Mascot.Source = new BitmapImage(new Uri("ms-appx:///Assets/Mascot/mascot.png"));
                Canvas.SetLeft(Mascot, -130);
                Canvas.SetTop(Mascot, 176);
                await MoveToAsync(155, 176, 700);
                // Land with the mascot's center over the custom close button.
                Mascot.Opacity = 1;
                await MoveToAsync(430, -58, 580);
                MascotScale.ScaleY = 0.72;
                MascotScale.ScaleX = 1.12;
                await Task.Delay(180);
                MascotScale.ScaleY = 1;
                MascotScale.ScaleX = 1;
                Mascot.Source = new BitmapImage(new Uri("ms-appx:///Assets/Mascot/mascot-smirk.png"));
                await Task.Delay(600);
                Mascot.Opacity = 0;
                await Task.Delay(180);
                CloseButton_Click(CloseButton, new RoutedEventArgs());
            }
            catch { }
            finally { if (!_closing) { _closing = true; Close(); } }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_closing) return;
            _closing = true;
            Close();
        }

        private async Task MoveToAsync(double x, double y, int duration)
        {
            double startX = Canvas.GetLeft(Mascot), startY = Canvas.GetTop(Mascot);
            int steps = 28;
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                Canvas.SetLeft(Mascot, startX + (x - startX) * t);
                Canvas.SetTop(Mascot, startY + (y - startY) * t - Math.Sin(t * Math.PI) * (y < startY ? 42 : 0));
                await Task.Delay(duration / steps);
            }
        }
    }
}

