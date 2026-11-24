using BlockBox.Models;
using Microsoft.UI.Xaml.Controls;
using System;
using System.ComponentModel;
using BlockBox.Helpers;

namespace BlockBox.Models
{
    public enum BuiltinTabKind
    {
        None = 0,
        Favorites,
        Recent,
    }

    public class MyTabItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string DisplayTitle => BuiltinKind switch
        {
            BuiltinTabKind.Favorites => LanguageService.Translate("收藏"),
            BuiltinTabKind.Recent => LanguageService.Translate("最近访问"),
            _ => Title
        };

        public void RefreshLocalizedTitle() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayTitle)));

        // 标签页标题ID
        public string Id { get; set; } = Guid.NewGuid().ToString();

        // 标签页标题（显示在标签上的文字）
        private string _title = string.Empty;
        public string Title
        {
            get => _title;
            set
            {
                if (_title == value) return;
                _title = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayTitle)));
            }
        }

        // 标签页图标（Segoe Fluent Icons 字体代码，比如 "\uE734"）
        private string _iconGlyph = "\uE8A5";
        public string IconGlyph
        {
            get => _iconGlyph;
            set
            {
                if (_iconGlyph == value) return;
                _iconGlyph = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconGlyph)));
            }
        }

        // 标签页里显示的内容（可以是一个 GridView、Page 等）
        public object? Content { get; set; }

        // 是否允许关闭
        public bool IsClosable { get; set; } = true;

        // 所属模式
        public AppMode BelongingMode { get; set; } = AppMode.Normal;

        public int SortOrder { get; set; } = 0;

        // 标记这是哪个内置标签
        public BuiltinTabKind BuiltinKind { get; set; } = BuiltinTabKind.None;

        // 备注
        private string _note = "";
        public string Note
        {
            get => _note;
            set
            {
                if (_note == value) return;
                _note = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Note)));
            }
        }

        // 是否固定在标签栏显示
        //   true → 显示在标签栏
        //   false → 显示在"展开按钮"的下拉菜单里
        public bool IsPinned { get; set; } = true;

        // ⭐ 新增：内容类型（跟 Category 一致，用于筛选这个标签只显示哪类文件）
        private ContentKind _contentKind = ContentKind.All;
        public ContentKind ContentKind
        {
            get => _contentKind;
            set
            {
                if (_contentKind == value) return;
                _contentKind = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ContentKind)));
            }
        }

        // ⭐ 新增：自定义扩展名列表（ContentKind == Custom 时用）
        private string _extensionList = "";
        public string ExtensionList
        {
            get => _extensionList;
            set
            {
                if (_extensionList == value) return;
                _extensionList = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExtensionList)));
            }
        }

        public SortMode SortMode { get; set; } = SortMode.Custom;
    }
}