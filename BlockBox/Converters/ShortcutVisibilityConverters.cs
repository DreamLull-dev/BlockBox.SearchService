using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace BlockBox.Converters
{
    // true → Visible，false → Collapsed
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value is bool b && b ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    // true → 0，false → 1（用于隐藏源卡片）
    public class BoolToInverseOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value is bool b && b ? 0.0 : 1.0;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}