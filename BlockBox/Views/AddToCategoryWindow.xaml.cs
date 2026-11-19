using BlockBox.Helpers;
using BlockBox.Models;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Graphics;

namespace BlockBox.Views
{
    /// <summary>
    /// “添加到”对话框：把选中的搜索结果加进某个分类。外观和交互沿用重命名窗口
    /// （TextInputWindow）：无边框、淡入缩放、确定/取消；中间的下拉选择换成了和搜索页
    /// 分类菜单同款的 Flyout 列表。
    /// </summary>
    public sealed partial class AddToCategoryWindow : Window
    {
        private readonly IReadOnlyList<Category> _categories;
        private readonly IReadOnlyList<string> _filePaths;
        private Category? _selectedCategory;
        private bool _isSized;

        public Category? Result { get; private set; }

        public AddToCategoryWindow(IReadOnlyList<Category> categories, IReadOnlyList<string> filePaths, Category? initial = null)
        {
            InitializeComponent();
            _categories = categories;
            _filePaths = filePaths;
            WindowAcrylicHelper.Apply(this);
            ModalWindowHelper.ConfigureBorderlessWindow(this);
            WindowFadeController.Prepare(this);

            TitleText.Text = LanguageService.Translate("添加到");
            PromptText.Text = LanguageService.Translate("选择分类");
            SaveButton.Content = LanguageService.Translate("确定");
            CancelButton.Content = LanguageService.Translate("取消");
            ToolTipService.SetToolTip(CategoryButton, LanguageService.Translate("选择分类"));

            // 优先选中主界面当前分类；它不接受这些文件时退到第一个可接受的分类。
            var preferred = initial is null ? null : categories.FirstOrDefault(c => c.Id == initial.Id && Accepts(c));
            _selectedCategory = preferred ?? categories.FirstOrDefault(Accepts);
            if (categories.Count > 0 && !categories.Any(Accepts))
                PromptText.Text = LanguageService.Translate("没有分类接受选中的文件类型");
            UpdateCategoryText();

            LanguageService.RegisterDynamicRoot(RootGrid);
            RootGrid.Loaded += RootGrid_Loaded;
        }

        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isSized) return;
            _isSized = true;
            try
            {
                if (AppWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.IsResizable = false;
                    presenter.IsMaximizable = false;
                    presenter.IsMinimizable = false;
                }

                double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
                AppWindow.Resize(new SizeInt32((int)(420 * scale), (int)(230 * scale)));
                // 居中到主窗体而不是搜索面板：面板贴着屏幕右缘，居中到它会把对话框挤出屏幕。
                if (App.MainWindow is { } main) ModalWindowHelper.CenterOnOwner(main, this);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[添加到分类] 尺寸调整失败: {ex}");
            }
            WindowFadeController.FadeAndScaleIn(this, RootGrid);
        }

        /// <summary>该分类是否接受当前全部待添加文件（按分类的类型/扩展名过滤）。</summary>
        private bool Accepts(Category category) =>
            _filePaths.All(path => ContentKindHelper.Matches(path, category.ContentKind, category.ExtensionList));

        private void UpdateCategoryText()
        {
            CategoryText.Text = _selectedCategory?.DisplayName ?? LanguageService.Translate("选择分类");
            CategoryIcon.Glyph = string.IsNullOrWhiteSpace(_selectedCategory?.IconGlyph)
                ? "\uE8B7"
                : _selectedCategory!.IconGlyph;
            SaveButton.IsEnabled = _selectedCategory is not null;
        }

        private void CategoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (_categories.Count == 0) return;

            var flyout = new Flyout();
            var panel = new StackPanel { Width = 240, Spacing = 0 };
            foreach (var category in _categories)
            {
                var target = category;
                bool accepts = Accepts(target);
                var content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    VerticalAlignment = VerticalAlignment.Center
                };
                content.Children.Add(new FontIcon
                {
                    Glyph = string.IsNullOrWhiteSpace(target.IconGlyph) ? "\uE8B7" : target.IconGlyph,
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = accepts ? 1 : 0.45
                });
                content.Children.Add(new TextBlock
                {
                    Text = target.DisplayName,
                    VerticalAlignment = VerticalAlignment.Center,
                    // 不接受这些文件的分类用淡色显示，并且不可点选。
                    Opacity = accepts ? 1 : 0.45
                });

                var button = new Button
                {
                    Content = content,
                    Width = 240,
                    Height = 36,
                    Padding = new Thickness(12, 0, 8, 0),
                    CornerRadius = new CornerRadius(8),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTabBarBrush"],
                    Style = (Style)Application.Current.Resources["MenuItemButtonStyle"],
                    IsEnabled = accepts
                };
                if (!accepts)
                    ToolTipService.SetToolTip(button, LanguageService.Translate("该分类不接受选中的文件类型"));
                LanguageService.ApplyGeneratedButtonSize(button);
                button.Width = 240;
                button.Height = 36;
                button.Click += (_, _) =>
                {
                    flyout.Hide();
                    _selectedCategory = target;
                    UpdateCategoryText();
                };
                panel.Children.Add(button);
            }

            flyout.Content = panel;
            flyout.FlyoutPresenterStyle = App.MainWindow is MainWindow main
                ? main.CreateModeFlyoutPresenterStyle()
                : new Style(typeof(FlyoutPresenter));
            flyout.ShouldConstrainToRootBounds = false;
            LanguageService.RegisterDynamicRoot(panel);
            flyout.ShowAt(CategoryButton, new FlyoutShowOptions
            {
                Position = new Windows.Foundation.Point(0, CategoryButton.ActualHeight),
                Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
                ShowMode = FlyoutShowMode.Standard
            });
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCategory is null) return;
            Result = _selectedCategory;
            WindowFadeController.CloseWithAnimation(this);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
            => WindowFadeController.CloseWithAnimation(this);
    }
}
