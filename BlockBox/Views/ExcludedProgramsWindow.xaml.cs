using BlockBox.Data;
using BlockBox.Helpers;
using BlockBox.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Linq;
using Windows.Graphics;

namespace BlockBox.Views
{
    public sealed partial class ExcludedProgramsWindow : Window
    {
        private bool _isSized;
        public event EventHandler<RecycleBinThumbnailRequestedEventArgs>? ThumbnailRequested;

        public ExcludedProgramsWindow()
        {
            InitializeComponent();
            BlockBox.Helpers.WindowAcrylicHelper.Apply(this);
            ModalWindowHelper.ConfigureCloseOnlyTitleBar(this);
            WindowFadeController.Prepare(this);
            LanguageService.RegisterDynamicRoot(RootGrid);
            LoadEntries();
            LanguageService.LanguageChanged += LanguageService_LanguageChanged;
            Closed += (_, _) => LanguageService.LanguageChanged -= LanguageService_LanguageChanged;
        }

        private void LanguageService_LanguageChanged(object? sender, EventArgs e)
        {
            LanguageService.Apply(RootGrid, LanguageService.CurrentLanguageCode);
            UpdateCount();
        }

        private void LoadEntries()
        {
            var entries = ExcludedProgramRepository.GetAll();
            EntriesList.ItemsSource = entries;
            UpdateCount();
            RemoveButton.IsEnabled = false;
        }

        private void UpdateCount()
        {
            int count = EntriesList.Items.Count;
            CountText.Text = count == 0 ? LanguageService.Translate("排除项为空")
                : LanguageService.TranslateFormat("共 {count} 项", ("{count}", count));
        }

        private void EntriesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => RemoveButton.IsEnabled = EntriesList.SelectedItems.Count > 0;

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in EntriesList.SelectedItems.OfType<ShortcutItem>().ToList())
                ExcludedProgramRepository.Remove(item.TargetPath);
            LoadEntries();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => WindowFadeController.CloseWithAnimation(this);

        private void EntriesList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue)
                return;
            if (args.ItemContainer is not FrameworkElement container || args.Item is not ShortcutItem item) return;

            // 模板子元素可能在容器事件之后才创建，延后一次补刷当前字体色。
            DispatcherQueue.TryEnqueue(() => WindowAcrylicHelper.ApplyCurrentTextColors(container));

            var icon = FindChild<FontIcon>(container, "EntryIcon");
            var image = FindChild<Image>(container, "EntryThumbnail");
            if (icon == null || image == null) return;
            icon.Visibility = Visibility.Visible;
            image.Visibility = Visibility.Collapsed;
            image.Source = null;
            image.Tag = item.Id;
            ThumbnailRequested?.Invoke(this, new RecycleBinThumbnailRequestedEventArgs(item, icon, image));
        }

        private static T? FindChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T element && string.Equals(element.Name, name, StringComparison.Ordinal)) return element;
                var found = FindChild<T>(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isSized) return;
            _isSized = true;
            LanguageService.Apply(RootGrid, LanguageService.CurrentLanguageCode);
            double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
            AppWindow.Resize(new SizeInt32((int)(820 * scale), (int)(560 * scale)));
            ModalWindowHelper.CenterOnOwner(App.MainWindow, this);
            WindowFadeController.FadeAndScaleIn(this, RootGrid);
        }
    }
}

