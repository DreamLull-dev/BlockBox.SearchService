using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;

namespace BlockBox.Helpers
{
    public static class TextBoxCornerRadiusHelper
    {
        public static readonly DependencyProperty DisableAutoCornerRadiusProperty =
            DependencyProperty.RegisterAttached(
                "DisableAutoCornerRadius", typeof(bool), typeof(TextBoxCornerRadiusHelper),
                new PropertyMetadata(false, OnDisableAutoCornerRadiusChanged));

        public static bool GetDisableAutoCornerRadius(DependencyObject element)
            => (bool)element.GetValue(DisableAutoCornerRadiusProperty);

        public static void SetDisableAutoCornerRadius(DependencyObject element, bool value)
            => element.SetValue(DisableAutoCornerRadiusProperty, value);

        private static void OnDisableAutoCornerRadiusChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            if (sender is TextBox textBox)
                Attach(textBox);
        }

        public static void Apply(DependencyObject? root)
        {
            if (root == null)
                return;

            ApplyCore(root, new HashSet<DependencyObject>());
        }

        private static void ApplyCore(DependencyObject root, HashSet<DependencyObject> visited)
        {
            if (!visited.Add(root))
                return;

            if (root is TextBox textBox)
                Attach(textBox);

            if (root is ContentControl { Content: DependencyObject content })
                ApplyCore(content, visited);

            if (root is ItemsControl itemsControl)
            {
                foreach (object item in itemsControl.Items)
                {
                    if (item is DependencyObject itemElement)
                        ApplyCore(itemElement, visited);
                }
            }

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
                ApplyCore(VisualTreeHelper.GetChild(root, i), visited);
        }

        public static void Attach(TextBox textBox)
        {
            textBox.SizeChanged -= TextBox_SizeChanged;
            if (GetDisableAutoCornerRadius(textBox))
                return;

            textBox.SizeChanged += TextBox_SizeChanged;
            UpdateCornerRadius(textBox);
        }

        private static void TextBox_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is TextBox textBox)
                UpdateCornerRadius(textBox);
        }

        private static void UpdateCornerRadius(TextBox textBox)
        {
            double radius = Math.Max(0, textBox.ActualHeight / 2 - 1);
            textBox.CornerRadius = new CornerRadius(radius);
        }
    }
}
