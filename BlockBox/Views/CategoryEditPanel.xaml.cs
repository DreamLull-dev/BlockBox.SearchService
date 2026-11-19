using BlockBox.Helpers;
using BlockBox.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BlockBox.Views
{
    public sealed partial class CategoryEditPanel : UserControl
    {
        // ⭐ 对外事件：用户点"创建/保存"或"取消"时触发
        public event EventHandler<CategoryEditResult>? Completed;

        private Category? _editing;
        private const string NewGroupTag = "__new__";
        private List<(string Id, string Name)> _availablePartitions = new();
        private string _customExtensions = "";
        private ContentKind? _previousKind;

        public CategoryEditPanel()
        {
            this.InitializeComponent();
            LanguageService.LanguageChanged += LanguageService_LanguageChanged;

            IconPickerHelper.Populate(IconPicker);
            IconPicker.SelectedIndex = 0;

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

        public void ApplyConfirmButtonBackground(Microsoft.UI.Xaml.Media.Brush brush)
        {
            ConfirmBtn.Background = brush;
        }
        public void ApplyConfirmButtonForeground(Microsoft.UI.Xaml.Media.Brush brush)
        {
            ConfirmBtn.Foreground = brush;
        }
        // ==================== 对外配置 ====================

        /// <summary>配置成"新建"模式</summary>
        public void ConfigureAsNew(List<(string Id, string Name)> partitions, string defaultPartitionId)
        {
            _editing = null;
            HeaderText.Text = LanguageService.Translate("新建分类");
            ConfirmBtn.Content = LanguageService.Translate("创建");

            NameBox.Text = "";
            PartitionNameBox.Text = "";
            IconPicker.SelectedIndex = 0;
            _customExtensions = "";
            _previousKind = null;
            KindPicker.SelectedIndex = 0;
            UpdateKindInput();

            SetAvailableGroups(partitions, defaultPartitionId);
            LanguageService.Apply(this, LanguageService.CurrentLanguageCode);
        }

        /// <summary>配置成"编辑"模式</summary>
        public void ConfigureAsEdit(Category cat, List<(string Id, string Name)> partitions)
        {
            _editing = cat;
            HeaderText.Text = LanguageService.Translate("编辑分类");
            ConfirmBtn.Content = LanguageService.Translate("保存");

            NameBox.Text = cat.Name;
            PartitionNameBox.Text = cat.PartitionName;

            int idx = IconPickerHelper.GetIndex(cat.IconGlyph);
            IconPicker.SelectedIndex = idx >= 0 ? idx : 0;

            _customExtensions = cat.ContentKind == ContentKind.Custom ? cat.ExtensionList : "";
            _previousKind = null;
            for (int i = 0; i < KindPicker.Items.Count; i++)
            {
                if (KindPicker.Items[i] is ComboBoxItem it
                    && it.Tag is ContentKind k && k == cat.ContentKind)
                {
                    KindPicker.SelectedIndex = i;
                    break;
                }
            }
            UpdateKindInput();

            SetAvailableGroups(partitions, cat.PartitionId);
            LanguageService.Apply(this, LanguageService.CurrentLanguageCode);
        }

        // ==================== 分区下拉 ====================

        private void SetAvailableGroups(List<(string Id, string Name)> partitions, string currentPartitionId)
        {
            GroupPicker.Items.Clear();
            _availablePartitions = partitions;
            foreach (var partition in partitions)
            {
                GroupPicker.Items.Add(new ComboBoxItem
                {
                    Content = partition.Name,
                    Tag = partition.Id
                });
            }

            GroupPicker.Items.Add(new ComboBoxItem
            {
                    Content = LanguageService.Translate("+ 新建分区"),
                Tag = NewGroupTag
            });

            for (int i = 0; i < GroupPicker.Items.Count; i++)
            {
                if (GroupPicker.Items[i] is ComboBoxItem item
                    && item.Tag is string v && v == currentPartitionId)
                {
                    GroupPicker.SelectedIndex = i;
                    UpdatePartitionNameInput();
                    return;
                }
            }

            GroupPicker.SelectedIndex = GroupPicker.Items.Count > 0 ? 0 : -1;
            UpdatePartitionNameInput();
        }

        // ==================== 事件 ====================

        private void IconPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int i = IconPicker.SelectedIndex;
            if (i >= 0)
                PreviewIcon.Glyph = IconPickerHelper.GetSelectedGlyph(IconPicker);
        }

        private void GroupPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdatePartitionNameInput();
        }

        private void UpdatePartitionNameInput()
        {
            bool creatingPartition = GroupPicker.SelectedItem is ComboBoxItem item
                && item.Tag as string == NewGroupTag;

            PartitionNameBox.IsReadOnly = !creatingPartition;
            if (creatingPartition)
            {
                if (_editing == null || string.IsNullOrWhiteSpace(PartitionNameBox.Text)
                    || _availablePartitions.Any(p => string.Equals(p.Name, PartitionNameBox.Text, StringComparison.OrdinalIgnoreCase)))
                {
                    PartitionNameBox.Text = string.Empty;
                }
                PartitionNameBox.PlaceholderText = LanguageService.Translate("请输入分区名");
                PartitionNameBox.Focus(FocusState.Programmatic);
                return;
            }

            PartitionNameBox.Text = GroupPicker.SelectedItem is ComboBoxItem selected
                ? selected.Content?.ToString() ?? string.Empty
                : string.Empty;
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
            string name = NameBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(name) && _editing == null)
                name = LanguageService.Translate("新建分类");
            if (string.IsNullOrEmpty(name)) { ShowTip(LanguageService.Translate("名字不能为空")); return; }

            if (GroupPicker.SelectedItem is not ComboBoxItem gItem)
            { ShowTip(LanguageService.Translate("请选择所属分区")); return; }

            string partitionId = gItem.Tag as string ?? "";
            string partitionName = "";
            if (partitionId == NewGroupTag)
            {
                partitionName = PartitionNameBox.Text?.Trim() ?? "";
                if (partitionName.Length == 0)
                    partitionName = LanguageService.Translate("新建分区");
                partitionId = "";
            }
            else
            {
                partitionName = gItem.Content?.ToString() ?? "";
            }

            ContentKind kind = ContentKind.All;
            string extList = "";
            if (KindPicker.SelectedItem is ComboBoxItem kItem && kItem.Tag is ContentKind k)
            {
                kind = k;
                extList = k == ContentKind.Custom ? (CustomExtBox.Text == _editing?.ExtensionList
                        ? _editing.ExtensionList : CustomExtBox.Text?.Trim() ?? "")
                    : _editing?.ContentKind == k ? _editing.ExtensionList : "";
            }

            Completed?.Invoke(this, new CategoryEditResult
            {
                IsConfirmed = true,
                Name = name,
                Icon = IconPickerHelper.GetSelectedGlyph(IconPicker),
                GroupIndex = _availablePartitions.FindIndex(p => p.Id == partitionId) + 1,
                PartitionId = partitionId,
                PartitionName = partitionName,
                Kind = kind,
                ExtList = extList,
                IsNew = _editing == null,
            });
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Completed?.Invoke(this, new CategoryEditResult { IsConfirmed = false });
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
            Completed?.Invoke(this, new CategoryEditResult { IsConfirmed = false });
        }
    }

    // ⭐ 对外结果
    public class CategoryEditResult
    {
        public bool IsConfirmed { get; set; }
        public string Name { get; set; } = "";
        public string Icon { get; set; } = "\uE71D";
        public int GroupIndex { get; set; } = 1;
        public string PartitionId { get; set; } = "";
        public string PartitionName { get; set; } = "";
        public ContentKind Kind { get; set; } = ContentKind.All;
        public string ExtList { get; set; } = "";
        public bool IsNew { get; set; }
    }
}
