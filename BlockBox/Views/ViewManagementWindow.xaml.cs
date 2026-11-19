using BlockBox.Data;
using BlockBox.Helpers;
using BlockBox.Models;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Windows.Graphics;

namespace BlockBox.Views
{
    public sealed partial class ViewManagementWindow : Window
    {
        private readonly Func<AppMode> _currentModeProvider;
        private bool _isSized;

        public ObservableCollection<ViewManagementItem> Modes { get; } = new();
        public event Action<AppMode>? ModeDeleted;

        public ViewManagementWindow(Func<AppMode> currentModeProvider)
        {
            InitializeComponent();
            BlockBox.Helpers.WindowAcrylicHelper.Apply(this);
            ModalWindowHelper.ConfigureCloseOnlyTitleBar(this);
            WindowFadeController.Prepare(this);
            _currentModeProvider = currentModeProvider;
            HeaderText.Text = LanguageService.Translate("删除视图");
            DescriptionText.Text = LanguageService.Translate(
                "仅可删除自定义视图。删除后会同时移除该视图保存的数据记录，电脑上的源文件不会删除。");
            LanguageService.RegisterDynamicRoot(RootGrid);
            LoadModes();
        }

        private void LoadModes()
        {
            Modes.Clear();
            AppMode currentMode = _currentModeProvider();
            foreach (var mode in ModeRepository.GetAll())
            {
                string displayName = ModeRepository.GetDisplayName(mode.Mode);
                if (mode.Mode == currentMode)
                {
                    displayName = LanguageService.TranslateFormat(
                        "{name}（当前）", ("{name}", displayName));
                }

                Modes.Add(new ViewManagementItem
                {
                    Mode = mode.Mode,
                    ModeName = ModeRepository.GetDisplayName(mode.Mode),
                    DisplayName = displayName,
                    ActionText = mode.IsBuiltIn
                        ? LanguageService.Translate("内置视图")
                        : LanguageService.Translate("删除"),
                    CanDelete = !mode.IsBuiltIn
                });
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: ViewManagementItem item }
                || !item.CanDelete)
            {
                return;
            }

            var confirmation = new ContentDialog
            {
                Title = LanguageService.Translate("删除视图"),
                Content = LanguageService.TranslateFormat(
                    "确定删除视图“{name}”吗？此操作会删除该视图保存的数据记录，但不会删除电脑上的源文件。",
                    ("{name}", item.ModeName)),
                PrimaryButtonText = LanguageService.Translate("删除"),
                CloseButtonText = LanguageService.Translate("取消"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot
            };
            LanguageService.ApplyDialog(confirmation, LanguageService.CurrentLanguageCode);
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
                return;

            try
            {
                if (!ModeRepository.Delete(item.Mode))
                    return;

                ModeDeleted?.Invoke(item.Mode);
                LoadModes();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[删除视图] 失败: {ex}");
                var errorDialog = new ContentDialog
                {
                    Title = LanguageService.Translate("删除视图"),
                    Content = LanguageService.Translate("删除视图失败"),
                    CloseButtonText = LanguageService.Translate("确定"),
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = RootGrid.XamlRoot
                };
                LanguageService.ApplyDialog(errorDialog, LanguageService.CurrentLanguageCode);
                await errorDialog.ShowAsync();
            }
        }

        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isSized)
                return;

            _isSized = true;
            try
            {
                double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
                int width = (int)(620 * scale);
                int height = (int)(480 * scale);
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
                Debug.WriteLine($"[视图管理] 窗口定位失败: {ex.Message}");
            }

            WindowFadeController.FadeAndScaleIn(this, RootGrid);
        }
    }

    public sealed class ViewManagementItem
    {
        public AppMode Mode { get; set; }
        public string ModeName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string ActionText { get; set; } = string.Empty;
        public bool CanDelete { get; set; }
    }
}

