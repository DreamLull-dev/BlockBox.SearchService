using BlockBox.Helpers;
using BlockBox.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BlockBox.Views
{
    public sealed partial class TabEditPanel : UserControl
    {
        // ⭐ 对外事件
        public event EventHandler<TabEditResult>? Completed;
        private MyTabItem? _editing;
        private string _customExtensions = "";
        private ContentKind? _previousKind;

        public TabEditPanel()
        {
            this.InitializeComponent();
            LanguageService.LanguageChanged += LanguageService_LanguageChanged;
            IconPickerHelper.Populate(IconPicker);
            IconPicker.SelectedIndex = 0;
            PreviewIcon.Glyph = IconPickerHelper.GetSelectedGlyph(IconPicker);

            foreach (ContentKind k in Enum.GetValues<ContentKind>())
            {
                KindPicker.Items.Add(new ComboBoxItem
                {
                    Content = LanguageService.Translate(ContentKindHelper.GetDisplayName(k)),
                    Tag = k
                });
            }
            KindPicker.SelectedIndex = 0;
        }

        private void LanguageService_LanguageChanged(object? sender, EventArgs e)
        {
            string selectedGlyph = IconPickerHelper.GetSelectedGlyph(IconPicker);
            Bindings?.Update();
            foreach (var item in KindPicker.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag is ContentKind kind)
                    item.Content = LanguageService.Translate(ContentKindHelper.GetDisplayName(kind));
            }
            IconPickerHelper.Populate(IconPicker);
            IconPicker.SelectedIndex = Math.Max(IconPickerHelper.GetIndex(selectedGlyph), 0);
        }


        // ==================== 对外配置 ====================

        public void ConfigureAsNew()
        {
            _editing = null;
            HeaderText.Text = LanguageService.Translate("新建标签");
            ConfirmBtn.Content = LanguageService.Translate("创建");
            DeleteBtn.Visibility = Visibility.Collapsed;

            TitleBox.Text = "";
            NoteBox.Text = "";
            IconPicker.SelectedIndex = 0;
            PreviewIcon.Glyph = IconPickerHelper.GetSelectedGlyph(IconPicker);
            _customExtensions = "";
            _previousKind = null;
            KindPicker.SelectedIndex = 0;
            UpdateKindInput();
            LanguageService.Apply(this, LanguageService.CurrentLanguageCode);
        }
        public void ApplyConfirmButtonBackground(Microsoft.UI.Xaml.Media.Brush brush)
        {
            ConfirmBtn.Background = brush;
        }
        public void ApplyConfirmButtonForeground(Microsoft.UI.Xaml.Media.Brush brush)
        {
            ConfirmBtn.Foreground = brush;
        }
        public void ConfigureAsEdit(MyTabItem tab)
        {
            _editing = tab;
            HeaderText.Text = LanguageService.Translate("编辑标签");
            ConfirmBtn.Content = LanguageService.Translate("保存");
            DeleteBtn.Visibility = Visibility.Visible;

            TitleBox.Text = tab.Title;
            NoteBox.Text = tab.Note;

            // ⭐ 回填图标
            int iconIdx = IconPickerHelper.GetIndex(tab.IconGlyph);
            IconPicker.SelectedIndex = iconIdx >= 0 ? iconIdx : 0;
            PreviewIcon.Glyph = IconPickerHelper.GetSelectedGlyph(IconPicker);

            _customExtensions = tab.ContentKind == ContentKind.Custom ? tab.ExtensionList : "";
            _previousKind = null;
            for (int i = 0; i < KindPicker.Items.Count; i++)
            {
                if (KindPicker.Items[i] is ComboBoxItem it
                    && it.Tag is ContentKind k && k == tab.ContentKind)
                {
                    KindPicker.SelectedIndex = i;
                    break;
                }
            }
            UpdateKindInput();
            LanguageService.Apply(this, LanguageService.CurrentLanguageCode);
        }

        // ==================== 事件 ====================
        private void IconPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int i = IconPicker.SelectedIndex;
            if (i >= 0)
                PreviewIcon.Glyph = IconPickerHelper.GetSelectedGlyph(IconPicker);
        }
        private void KindPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => UpdateKindInput();

        private void UpdateKindInput()
        {
            if (KindPicker.SelectedItem is not ComboBoxItem item || item.Tag is not ContentKind kind)
                return;

            if (_previousKind == ContentKind.Custom && kind != ContentKind.Custom)
                _customExtensions = CustomExtBox.Text;

            CustomExtBox.IsReadOnly = kind != ContentKind.Custom;
            CustomExtBox.Text = kind == ContentKind.Custom ? _customExtensions
                : kind == ContentKind.All ? "*"
                : string.Join(", ", ContentKindHelper.GetExtensions(kind));
            _previousKind = kind;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            string title = TitleBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(title) && _editing == null)
                title = LanguageService.Translate("新建标签");
            if (string.IsNullOrEmpty(title)) { ShowTip(LanguageService.Translate("标签名称不能为空")); return; }

            ContentKind kind = ContentKind.All;
            string extList = "";
            if (KindPicker.SelectedItem is ComboBoxItem kItem && kItem.Tag is ContentKind k)
            {
                kind = k;
                extList = k == ContentKind.Custom ? (CustomExtBox.Text == _editing?.ExtensionList
                        ? _editing.ExtensionList : CustomExtBox.Text?.Trim() ?? "")
                    : _editing?.ContentKind == k ? _editing.ExtensionList : "";
            }

            Completed?.Invoke(this, new TabEditResult
            {
                IsConfirmed = true,
                Title = title,
                Note = NoteBox.Text?.Trim() ?? "",
                Icon = IconPickerHelper.GetSelectedGlyph(IconPicker),
                Kind = kind,
                ExtList = extList,
                IsNew = _editing == null,
            });
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Completed?.Invoke(this, new TabEditResult { IsConfirmed = false });
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            Completed?.Invoke(this, new TabEditResult { IsDelete = true });
        }

        private async void ShowTip(string msg)
        {
            var dlg = new ContentDialog
            {
                Content = msg,
                CloseButtonText = LanguageService.Translate("好"),
                XamlRoot = RootGrid.XamlRoot
            };
            LanguageService.ApplyDialog(dlg, LanguageService.CurrentLanguageCode);
            await dlg.ShowAsync();
        }
        public void RequestCancel()
        {
            Completed?.Invoke(this, new TabEditResult { IsConfirmed = false });
        }
    }

    // ⭐ 对外结果
    public class TabEditResult
    {
        public bool IsConfirmed { get; set; }
        public bool IsDelete { get; set; }
        public string Title { get; set; } = "";
        public string Note { get; set; } = "";
        public string Icon { get; set; } = "\uE8A5";    // ⭐ 新增
        public ContentKind Kind { get; set; } = ContentKind.All;
        public string ExtList { get; set; } = "";
        public bool IsNew { get; set; }
    }
}