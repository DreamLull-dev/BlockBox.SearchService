using BlockBox.Data;
using BlockBox.Helpers;
using BlockBox.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.IO;
using Windows.Graphics;

namespace BlockBox.Views
{
    public sealed partial class ShortcutPropertiesWindow : Window
    {
        private readonly ShortcutItem _shortcut;
        private bool _isSized;
        public bool DeleteRequested { get; private set; }
        public event EventHandler? NameChanged;
        public event EventHandler<RecycleBinThumbnailRequestedEventArgs>? ThumbnailRequested;

        public ShortcutPropertiesWindow(ShortcutItem shortcut)
        {
            InitializeComponent();
            BlockBox.Helpers.WindowAcrylicHelper.Apply(this);
            _shortcut = shortcut;
            ModalWindowHelper.ConfigureCloseOnlyTitleBar(this);
            WindowFadeController.Prepare(this);
            LanguageService.RegisterDynamicRoot(RootGrid);
            RefreshDetails();
            LanguageService.LanguageChanged += LanguageService_LanguageChanged;
            Closed += (_, _) => LanguageService.LanguageChanged -= LanguageService_LanguageChanged;
        }

        private void LanguageService_LanguageChanged(object? sender, EventArgs e)
        {
            LanguageService.Apply(RootGrid, LanguageService.CurrentLanguageCode);
            RefreshDetails();
        }

        private void RefreshDetails()
        {
            string path = _shortcut.TargetPath;
            FileNameText.Text = string.IsNullOrWhiteSpace(path) ? _shortcut.Name : Path.GetFileName(path);
            CustomNameText.Text = LanguageService.TranslateFormat("自定义名称：{value}", ("{value}", _shortcut.Name));
            AddedText.Text = LanguageService.TranslateFormat("加入日期：{value}", ("{value}", _shortcut.CreatedTime.ToString("g")));
            string unavailable = LanguageService.Translate("不可用");
            string created = unavailable, modified = unavailable, size = unavailable;
            try
            {
                var file = new FileInfo(path);
                if (file.Exists)
                {
                    created = file.CreationTime.ToString("g");
                    modified = file.LastWriteTime.ToString("g");
                    size = LanguageService.TranslateFormat("{size} 字节", ("{size}", file.Length.ToString("N0")));
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[文件属性] 读取失败: {ex.Message}"); }
            CreatedText.Text = LanguageService.TranslateFormat("创建日期：{value}", ("{value}", created));
            ModifiedText.Text = LanguageService.TranslateFormat("修改日期：{value}", ("{value}", modified));
            LocationText.Text = LanguageService.TranslateFormat("文件所在位置：{value}", ("{value}", path));
            string backup = BackupPathHelper.MakeAbsolute(_shortcut.BackupPath);
            bool exists = _shortcut.IsBackedUp && !string.IsNullOrEmpty(backup) && File.Exists(backup);
            BackupText.Text = LanguageService.TranslateFormat("文件是否备份：{value}", ("{value}", LanguageService.Translate(exists ? "是" : "否")));
            OpenBackupButton.IsEnabled = exists;
            SizeText.Text = LanguageService.TranslateFormat("文件大小：{value}", ("{value}", size));
            FileIcon.Glyph = _shortcut.DisplayGlyph;
        }

        private async void RenameButton_Click(object sender, RoutedEventArgs e)
        {
            var input = new TextBox { Text = _shortcut.Name, MaxLength = 60 };
            var dialog = new ContentDialog
            {
                Title = LanguageService.Translate("修改名称"), Content = input,
                PrimaryButtonText = LanguageService.Translate("保存"), CloseButtonText = LanguageService.Translate("取消"),
                DefaultButton = ContentDialogButton.Close, XamlRoot = RootGrid.XamlRoot
            };
            LanguageService.ApplyDialog(dialog, LanguageService.CurrentLanguageCode);
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            string name = input.Text.Trim();
            if (string.IsNullOrEmpty(name) || name == _shortcut.Name) return;
            ShortcutRepository.UpdateName(_shortcut.Id, name);
            _shortcut.Name = name;
            RefreshDetails();
            NameChanged?.Invoke(this, EventArgs.Empty);
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            DeleteRequested = true;
            WindowFadeController.CloseWithAnimation(this);
        }

        private void OpenBackupButton_Click(object sender, RoutedEventArgs e)
        {
            string path = BackupPathHelper.MakeAbsolute(_shortcut.BackupPath);
            if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => WindowFadeController.CloseWithAnimation(this);

        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isSized) return;
            _isSized = true;
            LanguageService.Apply(RootGrid, LanguageService.CurrentLanguageCode);
            double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
            AppWindow.Resize(new SizeInt32((int)(540 * scale), (int)(700 * scale)));
            ModalWindowHelper.CenterOnOwner(App.MainWindow, this);
            WindowFadeController.FadeAndScaleIn(this, RootGrid);
            FileImage.Tag = _shortcut.Id;
            ThumbnailRequested?.Invoke(this, new RecycleBinThumbnailRequestedEventArgs(_shortcut, FileIcon, FileImage));
        }
    }
}

