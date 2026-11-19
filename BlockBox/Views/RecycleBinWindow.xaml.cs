using BlockBox.Data;
using BlockBox.Helpers;
using BlockBox.Models;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Graphics;

namespace BlockBox.Views
{
    public sealed partial class RecycleBinWindow : Window
    {
        private readonly bool _confirmOperations;
        private bool _isSized;

        public event EventHandler? DataChanged;
        public event EventHandler<RecycleBinThumbnailRequestedEventArgs>? ThumbnailRequested;

        public RecycleBinWindow(bool confirmOperations)
        {
            InitializeComponent();
            BlockBox.Helpers.WindowAcrylicHelper.Apply(this);
            ModalWindowHelper.ConfigureCloseOnlyTitleBar(this);
            WindowFadeController.Prepare(this);
            _confirmOperations = confirmOperations;
            HeaderText.Text = LanguageService.Translate("回收站");
            EmptyButton.Content = LanguageService.Translate("清空回收站");
            RestoreButton.Content = LanguageService.Translate("恢复");
            DeleteButton.Content = LanguageService.Translate("永久删除");
            LanguageService.RegisterDynamicRoot(RootGrid);
            LoadEntries();
        }

        private void LoadEntries()
        {
            var entries = RecycleBinRepository.GetAll();
            EntriesList.ItemsSource = entries;
            CountText.Text = entries.Count == 0
                ? LanguageService.Translate("回收站为空")
                : LanguageService.TranslateFormat("共 {count} 项", ("{count}", entries.Count.ToString()));
            EmptyButton.IsEnabled = entries.Count > 0;
            UpdateSelectionButtons();
        }

        private void EntriesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => UpdateSelectionButtons();

        private void UpdateSelectionButtons()
        {
            bool hasSelection = EntriesList.SelectedItem is RecycleBinEntry;
            RestoreButton.IsEnabled = hasSelection;
            DeleteButton.IsEnabled = hasSelection;
        }

        private async void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (EntriesList.SelectedItem is not RecycleBinEntry selected)
                return;

            bool restored;
            try
            {
                restored = RecycleBinRepository.Restore(selected.Shortcut.Id);
            }
            catch
            {
                restored = false;
            }

            if (!restored)
            {
                string? categoryId = await ChooseRecycleBinDestinationAsync(selected.Shortcut);
                if (categoryId == null)
                    return;

                try
                {
                    restored = RecycleBinRepository.Restore(selected.Shortcut.Id, categoryId);
                }
                catch
                {
                    restored = false;
                }
            }

            if (!restored)
            {
                await ShowMessageAsync(LanguageService.Translate("恢复失败"));
                return;
            }

            LoadEntries();
            DataChanged?.Invoke(this, EventArgs.Empty);
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (EntriesList.SelectedItem is not RecycleBinEntry selected)
                return;

            if (_confirmOperations && !await ConfirmAsync(
                    "永久删除",
                    LanguageService.TranslateFormat("确定永久删除「{name}」吗？", ("{name}", selected.Shortcut.Name)),
                    "永久删除"))
            {
                return;
            }

            try
            {
                DeleteBackupIfUnreferenced(RecycleBinRepository.PermanentlyDelete(selected.Shortcut.Id));
                LoadEntries();
                DataChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[回收站] 永久删除失败: {ex}");
                await ShowMessageAsync(LanguageService.Translate("永久删除失败"));
            }
        }

        private async void EmptyButton_Click(object sender, RoutedEventArgs e)
        {
            if (EntriesList.Items.Count == 0)
                return;

            if (_confirmOperations && !await ConfirmAsync(
                    "清空回收站",
                    LanguageService.Translate("永久删除回收站中的所有记录吗？"),
                    "清空"))
            {
                return;
            }

            try
            {
                foreach (string backupPath in RecycleBinRepository.Empty())
                    DeleteBackupIfUnreferenced(backupPath);
                LoadEntries();
                DataChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[回收站] 清空失败: {ex}");
                await ShowMessageAsync(LanguageService.Translate("清空回收站失败"));
            }
        }

        private async Task<string?> ChooseRecycleBinDestinationAsync(ShortcutItem shortcut)
        {
            var categories = ModeRepository.GetAllModes()
                .SelectMany(mode => CategoryRepository.GetVisible(mode))
                .Where(category => category.IsShared == shortcut.IsShared
                    && !string.Equals(category.Id, shortcut.CategoryId, StringComparison.OrdinalIgnoreCase))
                .GroupBy(category => category.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            if (categories.Count == 0)
            {
                await ShowMessageAsync(LanguageService.Translate("没有可用于恢复的兼容分类。"));
                return null;
            }

            var list = new ListView
            {
                ItemsSource = categories,
                SelectionMode = ListViewSelectionMode.Single,
                ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                    "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
                    "<TextBlock Padding='8,6' Text='{Binding DisplayName}'/>" +
                    "</DataTemplate>")
            };
            var dialog = new ContentDialog
            {
                Title = LanguageService.Translate("选择恢复分类"),
                Content = list,
                PrimaryButtonText = LanguageService.Translate("恢复到此分类"),
                CloseButtonText = LanguageService.Translate("取消"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot
            };
            LanguageService.ApplyDialog(dialog, LanguageService.CurrentLanguageCode);
            return await dialog.ShowAsync() == ContentDialogResult.Primary
                ? (list.SelectedItem as Category)?.Id
                : null;
        }

        private async Task<bool> ConfirmAsync(string title, string message, string action)
        {
            var dialog = new ContentDialog
            {
                Title = LanguageService.Translate(title),
                Content = message,
                PrimaryButtonText = LanguageService.Translate(action),
                CloseButtonText = LanguageService.Translate("取消"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot
            };
            LanguageService.ApplyDialog(dialog, LanguageService.CurrentLanguageCode);
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private async Task ShowMessageAsync(string message)
        {
            var dialog = new ContentDialog
            {
                Content = message,
                CloseButtonText = LanguageService.Translate("确定"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot
            };
            LanguageService.ApplyDialog(dialog, LanguageService.CurrentLanguageCode);
            await dialog.ShowAsync();
        }

        private static void DeleteBackupIfUnreferenced(string? backupPath)
        {
            if (string.IsNullOrWhiteSpace(backupPath)
                || ShortcutRepository.HasBackupReference(backupPath))
            {
                return;
            }

            string path = BackupPathHelper.MakeAbsolute(backupPath);
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private void EntriesList_ContainerContentChanging(
            ListViewBase sender,
            ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer is not FrameworkElement element
                || args.Item is not RecycleBinEntry entry)
            {
                return;
            }

            var icon = FindChildByTag<FontIcon>(element, "RecycleBinIcon");
            var image = FindChildByTag<Image>(element, "RecycleBinThumbnail");
            if (icon == null || image == null)
                return;

            icon.Visibility = Visibility.Visible;
            image.Visibility = Visibility.Collapsed;
            image.Source = null;
            image.Tag = entry.Shortcut.Id;
            ThumbnailRequested?.Invoke(this,
                new RecycleBinThumbnailRequestedEventArgs(entry.Shortcut, icon, image));
        }

        private static T? FindChildByTag<T>(DependencyObject parent, string tag) where T : FrameworkElement
        {
            int childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T element && string.Equals(element.Tag as string, tag, StringComparison.Ordinal))
                    return element;

                var result = FindChildByTag<T>(child, tag);
                if (result != null)
                    return result;
            }

            return null;
        }

        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isSized)
                return;

            _isSized = true;
            try
            {
                double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
                int width = (int)(820 * scale);
                int height = (int)(560 * scale);
                AppWindow.Resize(new SizeInt32(width, height));

                var mainWindow = App.MainWindow.AppWindow;
                var mainSize = mainWindow.Size;
                var mainPosition = mainWindow.Position;
                AppWindow.Move(new PointInt32(
                    mainPosition.X + (mainSize.Width - width) / 2,
                    mainPosition.Y + (mainSize.Height - height) / 2));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[回收站] 窗口定位失败: {ex.Message}");
            }

            WindowFadeController.FadeAndScaleIn(this, RootGrid);
        }
    }

    public sealed class RecycleBinThumbnailRequestedEventArgs : EventArgs
    {
        public ShortcutItem Shortcut { get; }
        public FontIcon Icon { get; }
        public Image Image { get; }

        public RecycleBinThumbnailRequestedEventArgs(ShortcutItem shortcut, FontIcon icon, Image image)
        {
            Shortcut = shortcut;
            Icon = icon;
            Image = image;
        }
    }
}

