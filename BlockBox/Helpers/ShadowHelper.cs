using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using System;
using System.Numerics;
using Windows.UI;

namespace BlockBox.Helpers
{
    public static class ShadowHelper
    {
        public static readonly DependencyProperty EnableHoverShadowProperty =
            DependencyProperty.RegisterAttached(
                "EnableHoverShadow", typeof(bool), typeof(ShadowHelper),
                new PropertyMetadata(false, OnChanged));

        public static void SetEnableHoverShadow(DependencyObject obj, bool value)
            => obj.SetValue(EnableHoverShadowProperty, value);
        public static bool GetEnableHoverShadow(DependencyObject obj)
            => (bool)obj.GetValue(EnableHoverShadowProperty);

        private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement element || e.NewValue is not true) return;
            element.Loaded += (_, _) => Attach(element);
        }

        private static void Attach(FrameworkElement element)
        {
            var compositor = ElementCompositionPreview.GetElementVisual(element).Compositor;

            // ⭐ 阴影必须挂在一个 SpriteVisual 上，不能直接挂 Visual
            var shadowVisual = compositor.CreateSpriteVisual();
            shadowVisual.Size = new Vector2(
                (float)element.ActualWidth,
                (float)element.ActualHeight);
            shadowVisual.Offset = Vector3.Zero;

            var shadow = compositor.CreateDropShadow();
            shadow.BlurRadius = 16f;
            shadow.Opacity = 0f;
            shadow.Color = Colors.Black;
            shadow.Offset = new Vector3(0, 2, 0);
            shadowVisual.Shadow = shadow;

            // ⭐ 把 shadowVisual 作为元素的子视觉塞进去，它会画在内容下面
            ElementCompositionPreview.SetElementChildVisual(element, shadowVisual);

            // 尺寸跟随元素
            element.SizeChanged += (_, _) =>
            {
                shadowVisual.Size = new Vector2(
                    (float)element.ActualWidth,
                    (float)element.ActualHeight);
            };

            element.PointerEntered += (_, _) =>
            {
                var anim = compositor.CreateScalarKeyFrameAnimation();
                anim.InsertKeyFrame(1f, 0.35f);
                anim.Duration = TimeSpan.FromMilliseconds(150);
                shadow.StartAnimation(nameof(DropShadow.Opacity), anim);
            };
            element.PointerExited += (_, _) =>
            {
                var anim = compositor.CreateScalarKeyFrameAnimation();
                anim.InsertKeyFrame(1f, 0f);
                anim.Duration = TimeSpan.FromMilliseconds(150);
                shadow.StartAnimation(nameof(DropShadow.Opacity), anim);
            };
        }
    }
}