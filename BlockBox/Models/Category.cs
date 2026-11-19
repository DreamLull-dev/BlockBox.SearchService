using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;   // ⭐ 需要这个 using，用到 Visibility
using BlockBox.Helpers;

namespace BlockBox.Models
{
    public class Category : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public string Id { get; set; } = Guid.NewGuid().ToString();
        private string _name = "";
        public string Name { get => _name; set { if (_name == value) return; _name = value; PropertyChanged?.Invoke(this, new(nameof(Name))); } }
        public string DisplayName => LanguageService.Translate(Name);
        private string _iconGlyph = "\uE71D";
        public string IconGlyph { get => _iconGlyph; set { if (_iconGlyph == value) return; _iconGlyph = value; PropertyChanged?.Invoke(this, new(nameof(IconGlyph))); } }
        public bool IsUserCreated { get; set; } = false;
        public bool IsShared { get; set; }
        public AppMode? BelongingMode { get; set; } = null;
        public int GroupIndex { get; set; } = 0;
        public string PartitionId { get; set; } = "";
        public string PartitionName { get; set; } = "";
        public int SortOrder { get; set; } = 0;

        // ⭐ 新增
        public ContentKind ContentKind { get; set; } = ContentKind.All;
        public string ExtensionList { get; set; } = "";

        public SortMode SortMode { get; set; } = SortMode.Custom;

        // UI 用（不存库）
        public bool IsSeparator { get; set; } = false;
        public bool IsSectionHeader { get; set; }
        public string SectionHeaderText { get; set; } = "";
        public string LocalizedSectionHeaderText
        {
            get
            {
                const string prefix = "◆ ";
                return SectionHeaderText.StartsWith(prefix, StringComparison.Ordinal)
                    ? prefix + LanguageService.Translate(SectionHeaderText[prefix.Length..])
                    : LanguageService.Translate(SectionHeaderText);
            }
        }

        // ⭐ 新增：收起态用的紧凑标题（菱形 + 首字）
        public string CompactHeaderText
        {
            get
            {
                if (!IsSectionHeader) return SectionHeaderText;

                // "◆ 默认" → "默认" → 首字
                string name = (SectionHeaderText ?? "").TrimStart('◆', ' ', '\u3000').Trim();
                if (string.IsNullOrEmpty(name)) return "◆";

                // 取第一个"字"（对中文就是第一个汉字；对英文是首字母）
                // 如果名字只有 1 个字符，就直接用它
                string first = name.Length == 1
                    ? name
                    : name.Substring(0, 1);

                return "◆ " + first;   // "◆默"
            }
        }
        public string LocalizedCompactHeaderText
        {
            get
            {
                string localizedHeader = LocalizedSectionHeaderText.TrimStart('◆', ' ', '\u3000').Trim();
                return string.IsNullOrEmpty(localizedHeader)
                    ? "◆"
                    : "◆ " + localizedHeader[..1];
            }
        }

        public void RefreshLocalizedProperties()
        {
            PropertyChanged?.Invoke(this, new(nameof(DisplayName)));
            PropertyChanged?.Invoke(this, new(nameof(LocalizedSectionHeaderText)));
            PropertyChanged?.Invoke(this, new(nameof(LocalizedCompactHeaderText)));
        }
        private string _sectionHeaderGlyph = "\uE70D";
        public string SectionHeaderGlyph { get => _sectionHeaderGlyph; set { if (_sectionHeaderGlyph == value) return; _sectionHeaderGlyph = value; PropertyChanged?.Invoke(this, new(nameof(SectionHeaderGlyph))); } }
        public string SectionPartitionId { get; set; } = "";
        private bool _isPartitionCollapsed;
        public bool IsPartitionCollapsed
        {
            get => _isPartitionCollapsed;
            set { if (_isPartitionCollapsed == value) return; _isPartitionCollapsed = value; PropertyChanged?.Invoke(this, new(nameof(IsPartitionCollapsed))); PropertyChanged?.Invoke(this, new(nameof(ContentVisibility))); }
        }
        public Visibility ContentVisibility =>
     !IsSectionHeader && !IsSeparator && !IsPartitionCollapsed
         ? Visibility.Visible
         : Visibility.Collapsed;
        public Visibility SeparatorVisibility => IsSeparator ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SectionHeaderVisibility => IsSectionHeader ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SeparatorLineVisibility => IsSeparator && !IsSectionHeader ? Visibility.Visible : Visibility.Collapsed;
    }
}
