using BlockBox.Helpers;
using BlockBox.Data;
using BlockBox.Models;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.Graphics;

namespace BlockBox.Views
{
    public sealed partial class MissingFilesWindow : Window
    {
        private bool _isSized;
        private readonly IReadOnlyCollection<ShortcutItem> _missing;
        private readonly IReadOnlyDictionary<string, string> _categoryNames;
        private bool _closed;

        public int Result { get; private set; }

        public MissingFilesWindow(IReadOnlyCollection<ShortcutItem> missing,
            IReadOnlyDictionary<string, string> categoryNames)
        {
            InitializeComponent();
            BlockBox.Helpers.WindowAcrylicHelper.Apply(this);
            _missing = missing;
            _categoryNames = categoryNames;
            RefreshLanguage();
            LanguageService.LanguageChanged += LanguageService_LanguageChanged;
            Closed += (_, _) =>
            {
                _closed = true;
                LanguageService.LanguageChanged -= LanguageService_LanguageChanged;
            };

            ModalWindowHelper.ConfigureBorderlessWindow(this);
            WindowFadeController.Prepare(this);
        }

        private void LanguageService_LanguageChanged(object? sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(() => { if (!_closed) RefreshLanguage(); });
        }

        private void RefreshLanguage()
        {
            TitleText.Text = LanguageService.Translate("源文件已失效");
            SummaryText.Text = _missing.Count == 0
                ? LanguageService.Translate("未检测到失效文件。")
                : LanguageService.TranslateFormat("以下 {count} 项的原文件已找不到：",
                    ("{count}", _missing.Count));
            DeleteButton.Content = LanguageService.Translate("删除记录");
            UseBackupButton.Content = LanguageService.Translate("使用备份");
            LaterButton.Content = LanguageService.Translate("稍后再说");
            DeleteButton.IsEnabled = _missing.Count > 0;
            InstructionsText.Text = LanguageService.Translate(
                "• 删除记录：从分类中移除，备份文件保留 7 天后自动清理\n• 使用备份：保留记录，双击时打开备份文件");

            int backupsMissing = _missing.Count(item =>
                string.IsNullOrWhiteSpace(item.BackupPath)
                || !File.Exists(BackupPathHelper.MakeAbsolute(item.BackupPath)));
            BackupWarningText.Text = backupsMissing > 0
                ? LanguageService.TranslateFormat("其中 {count} 项没有可用的备份。",
                    ("{count}", backupsMissing.ToString()))
                : string.Empty;
            UseBackupButton.IsEnabled = backupsMissing < _missing.Count;

            MissingItemsList.ItemsSource = _missing.Select(shortcut =>
            {
                string category = _categoryNames.TryGetValue(shortcut.CategoryId, out var name) ? LanguageService.Translate(name) : LanguageService.Translate("未知分类");
                string scope = shortcut.IsShared ? LanguageService.Translate("共享分类") : ModeRepository.GetDisplayName(shortcut.BelongingMode);
                return $"{shortcut.Name}（{category}，{scope}）\n{shortcut.TargetPath}";
            }).ToList();
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
                int width = (int)(660 * scale);
                int height = (int)(Math.Clamp(RootGrid.ActualHeight + 48, 420, 720) * scale);
                AppWindow.Resize(new SizeInt32(width, height));
                ModalWindowHelper.CenterOnOwner(App.MainWindow, this);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[失效文件窗口] 尺寸调整失败: {ex}");
            }
            WindowFadeController.FadeAndScaleIn(this, RootGrid);
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            Result = 1;
            WindowFadeController.CloseWithAnimation(this);
        }

        private void UseBackupButton_Click(object sender, RoutedEventArgs e)
        {
            Result = 2;
            WindowFadeController.CloseWithAnimation(this);
        }

        private void LaterButton_Click(object sender, RoutedEventArgs e)
        {
            Result = 0;
            WindowFadeController.CloseWithAnimation(this);
        }
    }
}

