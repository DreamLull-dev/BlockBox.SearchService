using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Runtime.CompilerServices;

namespace BlockBox.Helpers
{
    public static class FontSizeHelper
    {
        // 侧栏分类列表项文字（如分类名称、固定列表项）；侧栏树根使用此基准字号。
        public const double SidebarInitialFontSize = 13.0;
        // 顶部标签页标题及相关文字。
        public const double TabInitialFontSize = 13.0;
        // 主内容区普通文字及未单独指定基准的内容控件。
        public const double ContentInitialFontSize = 12.0;
        // 设置页主标题和各设置分区标题。
        public const double SettingsTitleInitialFontSize = 17.0;
        // 设置页普通正文、控件文字及设置页面根默认字号。
        public const double SettingsBodyInitialFontSize = 14.0;
        // 设置页辅助说明、小字及次要提示。
        public const double SettingsSmallInitialFontSize = 10.0;
        // 主内容区的小字号文字，如分区标题、模式切换按钮和搜索框。
        public const double ContentSmallFontSize = 12.0;
        // 主内容区提示类文字，如空状态提示和 Toast。
        public const double ContentHintFontSize = 13.0;
        // 新建/编辑分类、标签面板的标题。
        public const double DialogTitleFontSize = 12.0;
        // 分类、标签编辑面板中的字段标签和说明文字。
        public const double DialogBodyFontSize = 13.0;
        // 分类、标签编辑面板控件及标签悬浮预览中的按钮文字。
        public const double DialogControlFontSize = 12.0;
        // 预留的编辑面板选项文字基准。
        public const double DialogOptionFontSize = 13.0;
        // 标签悬浮预览中的标签标题。
        public const double TabPreviewTitleFontSize = 13.0;
        // 标签悬浮预览中的备注文字。
        public const double TabPreviewNoteFontSize = 10.0;
        // 标题栏文字；标题栏不参与全局字号档位缩放。
        public const double TitleBarFontSize = 12.0;

        private const int DefaultFontSizeStep = 2;
        private static readonly double[] FontSizeScaleSteps = { 0.8, 0.9, 1.0, 1.1, 1.2 };
        private static int _currentFontSizeStep = DefaultFontSizeStep;
        private static readonly ConditionalWeakTable<FrameworkElement, OriginalFontSize> OriginalSizes = new();
        private static readonly ConditionalWeakTable<FrameworkElement, RootSubscription> RootSubscriptions = new();
        private static readonly ConditionalWeakTable<FrameworkElement, SubscribedRoot> SubscribedRoots = new();

        public static int CurrentFontSizeStep
        {
            get => _currentFontSizeStep;
            set
            {
                value = Math.Clamp(value, 0, FontSizeScaleSteps.Length - 1);
                if (_currentFontSizeStep == value)
                    return;

                _currentFontSizeStep = value;
                FontSizeChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        public static double CurrentFontSizeOffset => (CurrentFontSizeStep - DefaultFontSizeStep) * 1;

        public static double GetScaledFontSize(double baseFontSize)
            => Math.Clamp(baseFontSize + CurrentFontSizeOffset, 6, 48);

        public static int GetFontSizeStepFromScale(double scale)
        {
            if (!double.IsFinite(scale))
                return DefaultFontSizeStep;

            int closestStep = DefaultFontSizeStep;
            double closestDistance = double.MaxValue;
            for (int i = 0; i < FontSizeScaleSteps.Length; i++)
            {
                double distance = Math.Abs(FontSizeScaleSteps[i] - scale);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestStep = i;
                }
            }

            return closestStep;
        }

        public static event EventHandler FontSizeChanged;

        public static readonly DependencyProperty ApplyGlobalFontScaleProperty =
            DependencyProperty.RegisterAttached(
                "ApplyGlobalFontScale",
                typeof(bool),
                typeof(FontSizeHelper),
                new PropertyMetadata(false, OnApplyGlobalFontScaleChanged));

        public static readonly DependencyProperty InitialFontSizeProperty =
            DependencyProperty.RegisterAttached(
                "InitialFontSize",
                typeof(double),
                typeof(FontSizeHelper),
                new PropertyMetadata(0.0));

        public static void SetApplyGlobalFontScale(DependencyObject obj, bool value)
            => obj.SetValue(ApplyGlobalFontScaleProperty, value);

        public static bool GetApplyGlobalFontScale(DependencyObject obj)
            => (bool)obj.GetValue(ApplyGlobalFontScaleProperty);

        public static void SetInitialFontSize(DependencyObject obj, double value)
            => obj.SetValue(InitialFontSizeProperty, value);

        public static double GetInitialFontSize(DependencyObject obj)
            => (double)obj.GetValue(InitialFontSizeProperty);

        public static void ApplyGlobalFontSizeToTree(DependencyObject root)
        {
            double initialFontSize = FindInitialFontSize(root);
            ApplyTreeFontSize(root, initialFontSize);
        }
        public static void ApplyGlobalFontSizeToPopup(FrameworkElement popupContent)
        {
            ApplyGlobalFontSizeToTree(popupContent);
        }
        public static void SetGlobalFontSizeStep(int value) => CurrentFontSizeStep = value;

        private static void OnApplyGlobalFontScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement root)
                return;

            if ((bool)e.NewValue)
            {
                if (RootSubscriptions.TryGetValue(root, out _))
                    return;

                EventHandler handler = (_, _) => ApplyGlobalFontSizeToTree(root);
                RootSubscriptions.Add(root, new RootSubscription(handler));
                root.Loaded += Root_Loaded;
                root.Unloaded += Root_Unloaded;
                if (root.IsLoaded)
                {
                    FontSizeChanged += handler;
                    SubscribedRoots.Add(root, new SubscribedRoot());
                    ApplyGlobalFontSizeToTree(root);
                }
            }
            else if (RootSubscriptions.TryGetValue(root, out var subscription))
            {
                FontSizeChanged -= subscription.Handler;
                SubscribedRoots.Remove(root);
                root.Loaded -= Root_Loaded;
                root.Unloaded -= Root_Unloaded;
                RootSubscriptions.Remove(root);

                // ⭐ 恢复原始字号
                RestoreFontSizeTree(root);
            }
        }
        private static void RestoreFontSizeTree(DependencyObject root)
        {
            if (root is FrameworkElement element && OriginalSizes.TryGetValue(element, out var original))
            {
                if (element is Control c) c.FontSize = original.Value;
                else if (element is TextBlock tb) tb.FontSize = original.Value;
            }

            if (root is FrameworkElement nestedRoot
                && !ReferenceEquals(root, null)
                && GetApplyGlobalFontScale(nestedRoot))
                return;   // 嵌套根自己会处理

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
                RestoreFontSizeTree(VisualTreeHelper.GetChild(root, i));
        }
        private static void Root_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement root
                || !RootSubscriptions.TryGetValue(root, out var subscription))
                return;

            if (!SubscribedRoots.TryGetValue(root, out _))
            {
                FontSizeChanged += subscription.Handler;
                SubscribedRoots.Add(root, new SubscribedRoot());
            }
            ApplyGlobalFontSizeToTree(root);
            root.DispatcherQueue.TryEnqueue(() =>
            {
                if (root.IsLoaded)
                    ApplyGlobalFontSizeToTree(root);
            });
        }

        private static void Root_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement root
                && RootSubscriptions.TryGetValue(root, out var subscription))
            {
                FontSizeChanged -= subscription.Handler;
                SubscribedRoots.Remove(root);
            }
        }

        private static void ApplyTreeFontSize(DependencyObject root, double inheritedInitialFontSize)
            => ApplyTreeFontSize(root, inheritedInitialFontSize, root);

        private static void ApplyTreeFontSize(
            DependencyObject root,
            double inheritedInitialFontSize,
            DependencyObject traversalRoot)
        {
            var elements = new System.Collections.Generic.List<(FrameworkElement Element, double InitialFontSize)>();
            CollectFontElements(
                root,
                inheritedInitialFontSize,
                traversalRoot,
                elements);

            foreach (var (element, initialFontSize) in elements)
                CaptureOriginalFontSize(element, initialFontSize);

            foreach (var (element, _) in elements)
                ApplyFontSize(element);
        }

        private static void CollectFontElements(
            DependencyObject root,
            double inheritedInitialFontSize,
            DependencyObject traversalRoot,
            System.Collections.Generic.List<(FrameworkElement Element, double InitialFontSize)> elements)
        {
            if (root is FrameworkElement nestedRoot
                && !ReferenceEquals(root, traversalRoot)
                && GetApplyGlobalFontScale(nestedRoot))
            {
                // ⭐ 关键：用嵌套根自己的 InitialFontSize 递归处理
                double nestedInitial = GetInitialFontSize(nestedRoot);
                if (nestedInitial <= 0) nestedInitial = inheritedInitialFontSize;

                // 把嵌套根本身也加进列表（它可能需要缩放）
                // 然后以 nestedInitial 为基准遍历它的子树
                CollectFontElements(nestedRoot, nestedInitial, nestedRoot, elements);
                return;
            }

            if (root is FontIcon || (root is FrameworkElement tagged && (tagged.Tag as string) == "SkipGlobalFontSize"))
                return;

            double initialFontSize = GetInitialFontSize(root);
            if (initialFontSize <= 0)
                initialFontSize = inheritedInitialFontSize;

            if (root is FrameworkElement element
                && element is not UserControl
                && (element is Control || element is TextBlock))
            {
                initialFontSize = GetOriginalFontSize(element, initialFontSize);
                elements.Add((element, initialFontSize));
            }

            if (root is TextBox or PasswordBox or RichEditBox)
                return;

            int childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
                CollectFontElements(
                    Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i),
                    initialFontSize,
                    traversalRoot,
                    elements);
        }

        private static double GetOriginalFontSize(FrameworkElement element, double inheritedInitialFontSize)
        {
            double knownInitialSize = GetKnownInitialFontSize(element);
            if (knownInitialSize > 0) return knownInitialSize;

            double explicitInitialSize = GetInitialFontSize(element);
            if (explicitInitialSize > 0) return Math.Clamp(explicitInitialSize, 6, 48);

            if (OriginalSizes.TryGetValue(element, out var original))
                return original.Value;

            if (IsInSettingsRegion(element))
            {
                double configuredSize = GetConfiguredFontSize(element);
                return GetSettingsTextBaseSize(configuredSize);
            }

            double localFontSize = GetLocalFontSize(element);
            return Math.Clamp(localFontSize > 0 ? localFontSize : inheritedInitialFontSize, 6, 48);
        }

        private static double GetConfiguredFontSize(FrameworkElement element)
        {
            double localFontSize = GetLocalFontSize(element);
            return localFontSize > 0 ? localFontSize : SettingsBodyInitialFontSize;
        }

        private static double GetLocalFontSize(FrameworkElement element)
        {
            object localValue;
            if (element is Control)
                localValue = element.ReadLocalValue(Control.FontSizeProperty);
            else if (element is TextBlock)
                localValue = element.ReadLocalValue(TextBlock.FontSizeProperty);
            else
                return 0;

            if (localValue is double fontSize && double.IsFinite(fontSize))
                return fontSize;

            return 0;
        }

        private static bool IsInSettingsRegion(DependencyObject element)
        {
            DependencyObject? current = element;
            while (current != null)
            {
                if (current is FrameworkElement frameworkElement
                    && GetApplyGlobalFontScale(frameworkElement)
                    && Math.Abs(GetInitialFontSize(frameworkElement) - SettingsBodyInitialFontSize) < 0.01)
                    return true;

                current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private static double GetSettingsTextBaseSize(double configuredSize)
        {
            if (configuredSize >= SettingsTitleInitialFontSize)
                return SettingsTitleInitialFontSize;

            if (configuredSize > 0 && configuredSize <= SettingsSmallInitialFontSize)
                return SettingsSmallInitialFontSize;

            return SettingsBodyInitialFontSize;
        }

        private static void CaptureOriginalFontSize(FrameworkElement element, double initialFontSize)
        {
            double clamped = Math.Clamp(initialFontSize, 6, 48);

            // 只在"元素自身显式声明 InitialFontSize"或"没缓存"时才写入
            bool explicitInit = GetInitialFontSize(element) > 0;
            bool knownInitial = GetKnownInitialFontSize(element) > 0;
            if (!OriginalSizes.TryGetValue(element, out _) || explicitInit || knownInitial)
            {
                OriginalSizes.Remove(element);
                OriginalSizes.Add(element, new OriginalFontSize(clamped));
            }
        }

        private static double GetKnownInitialFontSize(FrameworkElement element)
        {
            return element.Name switch
            {
                "TitleBarLogoText" or "TitleBarClock" => TitleBarFontSize,
                "SectionHeaderLabel" or "ModeSwitcherText" or "SearchBox" => ContentSmallFontSize,
                "ItemName" => ContentInitialFontSize,
                "EmptyHintText" or "ToastText" => ContentHintFontSize,
                _ => 0
            };
        }

        private static double FindInitialFontSize(DependencyObject element)
        {
            DependencyObject? current = element;
            while (current != null)
            {
                double initialFontSize = GetInitialFontSize(current);
                if (initialFontSize > 0)
                    return initialFontSize;
                current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
            }

            return ContentInitialFontSize;
        }

        private static void ApplyFontSize(FrameworkElement element)
        {
            if (!OriginalSizes.TryGetValue(element, out var original))
                return;

            double scaledSize = GetScaledFontSize(original.Value);
            if (element is Control control)
                control.FontSize = scaledSize;
            else if (element is TextBlock textBlock)
                textBlock.FontSize = scaledSize;
        }

        private sealed class OriginalFontSize
        {
            public OriginalFontSize(double value) => Value = value;
            public double Value { get; }
        }

        private sealed class RootSubscription
        {
            public RootSubscription(EventHandler handler) => Handler = handler;
            public EventHandler Handler { get; }
        }

        private sealed class SubscribedRoot { }
    }
}
