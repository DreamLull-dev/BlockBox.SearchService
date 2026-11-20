using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using Windows.UI;

namespace BlockBox.Helpers
{
    /// <summary>
    /// 应用内文字颜色的唯一取色入口。
    ///
    /// 规则：
    /// 1. 关闭“自动”时：严格使用用户选择的颜色，不做任何计算；
    /// 2. 开启“自动”时：把用户选择的颜色与控件实际背景做对比度比较，
    ///    亮度太接近就保留色相、自动提亮或压暗，直到达到可读对比度；
    /// 3. 赋值：每个控件在一次遍历中最多赋值一次，避免后面的遍历把前面的颜色覆盖掉。
    /// </summary>
    internal static class TextColorHelper
    {
        private static readonly Color OpaqueBlack = Color.FromArgb(255, 0, 0, 0);
        private static readonly Color OpaqueWhite = Color.FromArgb(255, 255, 255, 255);

        // WCAG AA 正文对比度阈值。
        private const double MinContrastRatio = 4.5;

        public static Color ParseColor(string? value, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            string hex = value.Trim().TrimStart('#');
            if (hex.Length == 6)
                hex = "FF" + hex;
            if (hex.Length != 8)
                return fallback;

            try
            {
                return Color.FromArgb(
                    byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
            catch
            {
                return fallback;
            }
        }

        public static bool IsOpaqueWhite(Color color)
            => color.A == 255 && color.R == 255 && color.G == 255 && color.B == 255;

        public static bool IsOpaqueBlack(Color color)
            => color.A == 255 && color.R == 0 && color.G == 0 && color.B == 0;

        /// <summary>
        /// 自动模式：背景太亮就压暗文字，背景太暗就提亮文字；保留原色相。
        /// 关闭自动模式：直接返回用户颜色。
        /// </summary>
        public static Color ResolveTextColor(Color background, Color textColor, bool adjustForContrast)
            => adjustForContrast ? EnsureContrast(textColor, background) : textColor;

        /// <summary>按背景亮度在黑/白之间取一个对比色（仅用于纯黑/纯白基础色）。</summary>
        public static Color GetContrastColor(Color background)
            => GetRelativeLuminance(background) >= 0.179 ? OpaqueBlack : OpaqueWhite;

        /// <summary>
        /// 如果文字与背景对比度不足，就在 HSL 空间里只调整明度，
        /// 找到“最接近原色、但已经足够可见”的那个颜色。
        /// </summary>
        public static Color EnsureContrast(Color textColor, Color background, double minContrastRatio = MinContrastRatio)
        {
            // 自动模式下文字必须可见：旧配置里可能存过半透明字体色，这里按不透明处理。
            if (textColor.A != 255)
                textColor = Color.FromArgb(255, textColor.R, textColor.G, textColor.B);

            if (GetContrastRatio(textColor, background) >= minContrastRatio)
                return textColor;

            // 默认的纯白/纯黑是“自动”的基础色：直接切到黑/白，
            // 避免白字在白底上被调成灰色这种不自然的结果。
            if (IsOpaqueWhite(textColor) || IsOpaqueBlack(textColor))
                return GetContrastColor(background);

            // 选择能提供更高对比度的方向：向黑压暗，或向白提亮。
            double blackContrast = GetContrastRatio(OpaqueBlack, background);
            double whiteContrast = GetContrastRatio(OpaqueWhite, background);
            bool darken = blackContrast >= whiteContrast;

            var (hue, saturation, lightness) = ToHsl(textColor);
            double goodLightness = darken ? 0 : 1;
            byte alpha = textColor.A;

            // 方向选错或背景处于中间灰时，端点也可能达不到阈值；
            // 此时返回端点，至少比原来的低对比颜色更清楚。
            Color endpoint = FromHsl(hue, saturation, goodLightness, alpha);
            if (GetContrastRatio(endpoint, background) < minContrastRatio)
                return endpoint;

            // 在“原始明度（不够）”与“端点（够）”之间二分，取最接近原色的可用明度。
            double badLightness = lightness;
            for (int i = 0; i < 24; i++)
            {
                double mid = (goodLightness + badLightness) / 2;
                Color candidate = FromHsl(hue, saturation, mid, alpha);
                if (GetContrastRatio(candidate, background) >= minContrastRatio)
                    goodLightness = mid;
                else
                    badLightness = mid;
            }

            return FromHsl(hue, saturation, goodLightness, alpha);
        }

        public static Color CompositeOver(Color foreground, Color background)
        {
            if (foreground.A == 0)
                return background;
            if (foreground.A == 255)
                return Color.FromArgb(255, foreground.R, foreground.G, foreground.B);

            double alpha = foreground.A / 255.0;
            byte Blend(byte front, byte back)
                => (byte)Math.Clamp(Math.Round(front * alpha + back * (1 - alpha)), 0, 255);

            return Color.FromArgb(
                255,
                Blend(foreground.R, background.R),
                Blend(foreground.G, background.G),
                Blend(foreground.B, background.B));
        }

        /// <summary>
        /// 从控件向上找它实际所在的背景色。半透明背景会逐层与父级背景合成，
        /// 这样设置页里“深色页面 + 白色下拉框”这类混合背景也能各自取到正确的文字色。
        /// </summary>
        public static Color GetEffectiveBackground(DependencyObject? element, Color fallback)
        {
            if (element == null)
                return fallback;

            var chain = new List<DependencyObject>(8);
            DependencyObject? current = element;
            while (current != null && chain.Count < 128)
            {
                chain.Add(current);
                current = VisualTreeHelper.GetParent(current);
            }

            Color effective = fallback;
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                if (!TryGetSolidBackground(chain[i], out Color background) || background.A == 0)
                    continue;

                effective = background.A == 255
                    ? Color.FromArgb(255, background.R, background.G, background.B)
                    : CompositeOver(background, effective);
            }

            return effective;
        }

        /// <summary>
        /// 对一棵视觉树统一设置文字颜色。
        /// </summary>
        /// <param name="textColor">用户选择的基础颜色。</param>
        /// <param name="adjustForContrast">true：按每个控件的实际背景提亮/压暗；false：严格使用原色。</param>
        /// <param name="fallbackBackground">找不到任何背景时使用的兜底背景。</param>
        /// <param name="skipSubtree">返回 true 的节点及其子树不再处理（例如设置页、自绘标签栏）。</param>
        public static void ApplyForegroundToTree(
            DependencyObject root,
            Color textColor,
            bool adjustForContrast,
            Color fallbackBackground,
            Func<DependencyObject, bool>? skipSubtree = null)
        {
            var brushCache = new Dictionary<Color, SolidColorBrush>();
            var contrastCache = new Dictionary<Color, Color>();
            ApplyForegroundCore(
                root,
                textColor,
                adjustForContrast,
                fallbackBackground,
                skipSubtree,
                brushCache,
                contrastCache);
        }

        private static void ApplyForegroundCore(
            DependencyObject root,
            Color textColor,
            bool adjustForContrast,
            Color fallbackBackground,
            Func<DependencyObject, bool>? skipSubtree,
            Dictionary<Color, SolidColorBrush> brushCache,
            Dictionary<Color, Color> contrastCache)
        {
            if (skipSubtree?.Invoke(root) == true)
                return;

            // 这些元素的颜色由专门的逻辑控制（例如设置页的主色按钮），
            // 统一取色时必须跳过整棵子树，避免和专用颜色互相覆盖。
            if (root is FrameworkElement element &&
                string.Equals(element.Tag as string, "SkipForeground", StringComparison.Ordinal))
            {
                return;
            }

            Color resolvedColor = textColor;
            if (adjustForContrast)
            {
                // 同一背景（例如设置页大量白色卡片）只计算一次对比度。
                Color background = GetEffectiveBackground(root, fallbackBackground);
                if (!contrastCache.TryGetValue(background, out resolvedColor))
                {
                    resolvedColor = EnsureContrast(textColor, background);
                    contrastCache[background] = resolvedColor;
                }
            }

            if (!brushCache.TryGetValue(resolvedColor, out SolidColorBrush? brush))
            {
                brush = new SolidColorBrush(resolvedColor);
                brushCache[resolvedColor] = brush;
            }

            if (root is Control control)
                control.Foreground = brush;
            else if (root is TextBlock textBlock)
                textBlock.Foreground = brush;
            else if (root is RichTextBlock richTextBlock)
                richTextBlock.Foreground = brush;
            else if (root is FontIcon fontIcon)
                fontIcon.Foreground = brush;

            // TextBox 的内容不在 TextBlock 上，占位符也要单独跟随。
            if (root is TextBox textBox)
                textBox.PlaceholderForeground = brush;

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                ApplyForegroundCore(
                    VisualTreeHelper.GetChild(root, i),
                    textColor,
                    adjustForContrast,
                    fallbackBackground,
                    skipSubtree,
                    brushCache,
                    contrastCache);
            }
        }

        private static bool TryGetSolidBackground(DependencyObject element, out Color color)
        {
            switch (element)
            {
                case Panel panel when panel.Background is SolidColorBrush panelBrush:
                    color = panelBrush.Color;
                    return true;
                case Border border when border.Background is SolidColorBrush borderBrush:
                    color = borderBrush.Color;
                    return true;
                case Control control:
                    if (control.Background is SolidColorBrush controlBrush)
                    {
                        color = controlBrush.Color;
                        return true;
                    }

                    // 应用固定为 Light 主题。按钮、输入框等没有显式 Background 时，
                    // 模板默认就是浅色表面，文字应从深色方向校正。
                    if (control.Background == null && IsLightSurfaceByDefault(control))
                    {
                        color = OpaqueWhite;
                        return true;
                    }
                    break;
            }

            color = default;
            return false;
        }

        private static bool IsLightSurfaceByDefault(Control control)
            => control is Button
                or CheckBox
                or RadioButton
                or ComboBox
                or TextBox
                or NumberBox
                or AutoSuggestBox;

        /// <summary>WCAG 对比度：(亮 + 0.05) / (暗 + 0.05)。</summary>
        public static double GetContrastRatio(Color a, Color b)
        {
            double lumA = GetRelativeLuminance(a);
            double lumB = GetRelativeLuminance(b);
            double lighter = Math.Max(lumA, lumB);
            double darker = Math.Min(lumA, lumB);
            return (lighter + 0.05) / (darker + 0.05);
        }

        private static double GetRelativeLuminance(Color color)
        {
            static double Linearize(byte channel)
            {
                double value = channel / 255.0;
                return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Linearize(color.R)
                 + 0.7152 * Linearize(color.G)
                 + 0.0722 * Linearize(color.B);
        }

        private static (double Hue, double Saturation, double Lightness) ToHsl(Color color)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double lightness = (max + min) / 2;
            double delta = max - min;

            if (delta < 1e-9)
                return (0, 0, lightness);

            double saturation = lightness > 0.5
                ? delta / (2 - max - min)
                : delta / (max + min);

            double hue;
            if (max == r)
                hue = (g - b) / delta + (g < b ? 6 : 0);
            else if (max == g)
                hue = (b - r) / delta + 2;
            else
                hue = (r - g) / delta + 4;

            return (hue / 6, saturation, lightness);
        }

        private static Color FromHsl(double hue, double saturation, double lightness, byte alpha)
        {
            double r, g, b;

            if (saturation <= 0)
            {
                r = g = b = lightness;
            }
            else
            {
                double q = lightness < 0.5
                    ? lightness * (1 + saturation)
                    : lightness + saturation - lightness * saturation;
                double p = 2 * lightness - q;

                r = HueToRgb(p, q, hue + 1.0 / 3);
                g = HueToRgb(p, q, hue);
                b = HueToRgb(p, q, hue - 1.0 / 3);
            }

            static byte ToByte(double value)
                => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);

            return Color.FromArgb(alpha, ToByte(r), ToByte(g), ToByte(b));
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
    }
}
