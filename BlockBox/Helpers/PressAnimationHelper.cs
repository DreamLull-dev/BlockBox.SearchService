using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Runtime.CompilerServices;

namespace BlockBox.Helpers
{
    public static class PressAnimationHelper
    {
        // 记录已经挂过事件的元素，避免重复挂
        private static readonly ConditionalWeakTable<UIElement, object> _attachedElements = new();

        /// <summary>
        /// 播放一次"弹一下"动画：1 → scale → scaleDown → 1
        /// </summary>
        /// <param name="element">目标控件</param>
        /// <param name="scale">放大峰值，默认 1.10</param>
        /// <param name="scaleDown">回弹谷值，默认 0.95</param>
        public static void PlayBounce(UIElement element, double scale = 1.02, double scaleDown = 0.98)
        {
            if (element == null) return;

            // ==== 1. 获取或创建 ScaleTransform ====
            ScaleTransform? st = null;

            if (element.RenderTransform is ScaleTransform directScale)
            {
                st = directScale;
            }
            else if (element.RenderTransform is TransformGroup group)
            {
                foreach (var child in group.Children)
                {
                    if (child is ScaleTransform s) { st = s; break; }
                }
                if (st == null)
                {
                    st = new ScaleTransform { ScaleX = 1, ScaleY = 1 };
                    group.Children.Add(st);
                }
            }
            else
            {
                st = new ScaleTransform { ScaleX = 1, ScaleY = 1 };
                element.RenderTransform = st;
            }

            element.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);

            // ==== 2. 三段时长 ====
            var upDur = TimeSpan.FromMilliseconds(100);
            var downDur = TimeSpan.FromMilliseconds(120);
            var backDur = TimeSpan.FromMilliseconds(120);
            var totalDur = upDur + downDur + backDur;

            var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
            var easeInOut = new CubicEase { EasingMode = EasingMode.EaseInOut };

            var sb = new Storyboard();

            // ==== 3. ScaleX：关键帧动画（1 → scale → scaleDown → 1） ====
            var scaleXAnim = new DoubleAnimationUsingKeyFrames
            {
                Duration = totalDur
            };
            scaleXAnim.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero),
                Value = 1.0
            });
            scaleXAnim.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(upDur),
                Value = scale,
                EasingFunction = easeOut
            });
            scaleXAnim.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(upDur + downDur),
                Value = scaleDown,
                EasingFunction = easeInOut
            });
            scaleXAnim.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(totalDur),
                Value = 1.0,
                EasingFunction = easeOut
            });
            Storyboard.SetTarget(scaleXAnim, st);
            Storyboard.SetTargetProperty(scaleXAnim, "ScaleX");
            sb.Children.Add(scaleXAnim);

            // ==== 4. ScaleY：关键帧动画（和 ScaleX 同步） ====
            var scaleYAnim = new DoubleAnimationUsingKeyFrames
            {
                Duration = totalDur
            };
            scaleYAnim.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero),
                Value = 1.0
            });
            scaleYAnim.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(upDur),
                Value = scale,
                EasingFunction = easeOut
            });
            scaleYAnim.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(upDur + downDur),
                Value = scaleDown,
                EasingFunction = easeInOut
            });
            scaleYAnim.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(totalDur),
                Value = 1.0,
                EasingFunction = easeOut
            });
            Storyboard.SetTarget(scaleYAnim, st);
            Storyboard.SetTargetProperty(scaleYAnim, "ScaleY");
            sb.Children.Add(scaleYAnim);

            sb.Begin();
        }

        // ==================== 附加属性：XAML 一行启用 ====================

        public static readonly DependencyProperty EnableBounceProperty =
            DependencyProperty.RegisterAttached(
                "EnableBounce",
                typeof(bool),
                typeof(PressAnimationHelper),
                new PropertyMetadata(false, OnEnableBounceChanged));

        public static void SetEnableBounce(DependencyObject obj, bool value)
            => obj.SetValue(EnableBounceProperty, value);

        public static bool GetEnableBounce(DependencyObject obj)
            => (bool)obj.GetValue(EnableBounceProperty);

        private static void OnEnableBounceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement element) return;

            if (!(bool)e.NewValue)
            {
                // 属性被设为 false，可以不处理，因为事件监听还在
                return;
            }

            // 防重复挂载
            if (_attachedElements.TryGetValue(element, out _)) return;
            _attachedElements.Add(element, new object());

            if (element is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase btn)
            {
                // ButtonBase 系（Button / ToggleButton / RepeatButton / DropDownButton 等）
                btn.Click += (s, args) => PlayBounce(btn);
            }
            else
            {
                // 其他可点击控件（GridViewItem / ListViewItem / 自定义卡片等）
                // 用 Tapped 兜底
                element.Tapped += (s, args) => PlayBounce(element);
            }
        }
    }
}