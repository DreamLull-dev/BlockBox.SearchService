using BlockBox.Helpers;
using System.ComponentModel;

namespace BlockBox.Views
{
    public sealed class CategoryHistoryTabItem : INotifyPropertyChanged
    {
        public CategoryHistoryTabItem(string category, string iconGlyph)
        {
            Category = category;
            IconGlyph = iconGlyph;
        }

        public string Category { get; }
        public string IconGlyph { get; }
        public string DisplayTitle => LanguageService.Translate(Category);

        public event PropertyChangedEventHandler? PropertyChanged;

        public void RefreshLocalizedTitle() =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayTitle)));
    }
}
