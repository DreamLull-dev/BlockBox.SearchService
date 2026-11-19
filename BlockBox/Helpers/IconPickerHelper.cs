using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.Linq;

namespace BlockBox.Helpers
{
    public static class IconPickerHelper
    {
        private static readonly (string Name, string Glyph)[] Options =
        {
            ("默认", "\uE71D"),
            ("文件夹", "\uE8B7"),
            ("文档", "\uE8A5"),
            ("应用", "\uECAA"),
            ("图片", "\uE7C1"),
            ("音乐", "\uE8D6"),
            ("游戏", "\uE7FC"),
            ("收藏", "\uE734"),
            ("历史", "\uE81C"),
            ("开发", "\uE8F1"),
            ("视频", "\uE714"),
            ("设置", "\uE713"),
            ("下载", "\uE896"),
            ("链接", "\uE71B"),
            ("联系人", "\uE716"),
            ("云端", "\uE753")
        };

        public static void Populate(ComboBox picker)
        {
            picker.Items.Clear();
            foreach (var option in Options)
            {
               picker.Items.Add(new ComboBoxItem
                {
                    Tag = option.Glyph,
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            new FontIcon { Glyph = option.Glyph, FontSize = 14 },
                            new TextBlock { Text = LanguageService.Translate(option.Name), VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center }
                        }
                    }
                });
            }
        }

        public static int GetIndex(string glyph)
            => System.Array.FindIndex(Options, option => option.Glyph == glyph);

        public static string GetSelectedGlyph(ComboBox picker)
            => picker.SelectedItem is ComboBoxItem item && item.Tag is string glyph
                ? glyph
                : Options[0].Glyph;
    }
}
