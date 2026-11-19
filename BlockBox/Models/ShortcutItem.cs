using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace BlockBox.Models
{
    public class ShortcutItem : INotifyPropertyChanged
    {
        // ========== 你原有的所有字段，保持不变 ==========

        public string Id { get; set; } = Guid.NewGuid().ToString();
        private string _name = string.Empty;
        public string Name { get => _name; set { if (_name == value) return; _name = value; OnPropertyChanged(); } }
        public string TargetPath { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public string WorkingDirectory { get; set; } = string.Empty;
        public string IconPath { get; set; } = string.Empty;
        public string CategoryId { get; set; } = string.Empty;
        public string TitleId { get; set; } = string.Empty;
        public bool IsFile { get; set; } = false;
        public AppMode BelongingMode { get; set; } = AppMode.Normal;
        public bool IsShared { get; set; }
        public int SortOrder { get; set; } = 0;
        private bool _isFavorite;
        public bool IsFavorite { get => _isFavorite; set { if (_isFavorite == value) return; _isFavorite = value; OnPropertyChanged(); } }
        public DateTime? FavoriteTime { get; set; }
        public DateTime? LastAccessedTime { get; set; }
        public int AccessCount { get; set; } = 0;
        public string BackupPath { get; set; } = "";
        public string SourceMd5 { get; set; } = "";
        public SourceState SourceState { get; set; } = SourceState.Unknown;
        public DateTime? LastVerifiedTime { get; set; }
        public bool IsBackedUp { get; set; } = true;
        public string DisplayGlyph => BlockBox.Helpers.FileIconHelper.GetGlyphForFile(TargetPath);
        public DateTime CreatedTime { get; set; } = DateTime.Now;
        public string UwpLogoPath { get; set; } = "";

        // ========== ⚠️ 只新增下面这两部分 ==========

        // 拖拽状态（不存数据库，只用于 UI 绑定）
        private bool _isDragging;
        public bool IsDragging
        {
            get => _isDragging;
            set
            {
                if (_isDragging == value) return;
                _isDragging = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DraggedOpacity));                 // ⚠️ 新增
                OnPropertyChanged(nameof(DragPlaceholderVisibility));      // ⚠️ 新增
            }
        }

        // INotifyPropertyChanged 实现
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // 拖拽时透明（隐藏源卡片）
        public double DraggedOpacity => IsDragging ? 0.0 : 1.0;

        // 拖拽时显示占位框
        public Microsoft.UI.Xaml.Visibility DragPlaceholderVisibility
            => IsDragging ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }
}
