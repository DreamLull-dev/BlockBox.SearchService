using BlockBox.Helpers;
using BlockBox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System;
using System.Linq;
using Windows.Graphics;
using Windows.UI;

namespace BlockBox.Views
{
    public sealed partial class ProgramCategoryHistoryWindow : Window
    {
        private bool _ready;
        private bool _sized;
        private string _selectedCategory = "办公";
        private readonly SolidColorBrush _selectedBrush;
        private readonly SolidColorBrush _normalTextBrush;
        private readonly SolidColorBrush _selectedTextBrush;
        private readonly SolidColorBrush _selectedBorderBrush;
        private readonly SolidColorBrush _contentAreaBrush;
        private readonly SolidColorBrush _transparentBrush = new(Microsoft.UI.Colors.Transparent);

        public ProgramCategoryHistoryWindow()
        {
            InitializeComponent();
            BlockBox.Helpers.WindowAcrylicHelper.Apply(this);
            ModalWindowHelper.ConfigureCloseOnlyTitleBar(this);
            WindowFadeController.Prepare(this);
            var owner = (MainWindow)App.MainWindow;
            var tabColor = owner.GetTabBarBackgroundColor();
            var contentBackground = owner.GetMainContentAreaBackgroundColor();
            _selectedBrush = new SolidColorBrush(contentBackground);
            _contentAreaBrush = new SolidColorBrush(CalculateBrightBorderColor(contentBackground));
            var (normalText, selectedText) = owner.GetTabForegroundColors();
            _normalTextBrush = new SolidColorBrush(normalText);
            _selectedTextBrush = new SolidColorBrush(selectedText);
            _selectedBorderBrush = new SolidColorBrush(CalculateBrightBorderColor(owner.GetMainContentAreaBackgroundColor()));
            RootGrid.Background = _selectedBrush;
            TabBarGrid.Background = new SolidColorBrush(tabColor);
            HeaderText.Foreground = DescriptionText.Foreground = StatusText.Foreground = _selectedTextBrush;
            EntriesList.Background = _contentAreaBrush;
            LanguageService.LanguageChanged += LanguageService_LanguageChanged;
            Closed += (_, _) => LanguageService.LanguageChanged -= LanguageService_LanguageChanged;
            Localize();
            _ready = true;
            UpdateCategoryButtons();
            LoadEntries();
        }

        private void Localize()
        {
            HeaderText.Text = LanguageService.Translate("程序分类记录");
            OfficeCategoryText.Text = LanguageService.Translate("办公");
            GamesCategoryText.Text = LanguageService.Translate("游戏");
            MusicCategoryText.Text = LanguageService.Translate("音乐");
            SearchBox.PlaceholderText = LanguageService.Translate("搜索...");
            DescriptionText.Text = LanguageService.Translate("记录独立于数据库保存；更改规则不会移除已添加的程序。");
            RemoveButton.Content = LanguageService.Translate("仅移除记录");
            ExcludeButton.Content = LanguageService.Translate("删除记录并禁止自动分类");
            UnblockButton.Content = LanguageService.Translate("解除分类排除");
            CloseButton.Content = LanguageService.Translate("关闭");
        }

        private static Color CalculateBrightBorderColor(Color background)
        {
            static byte Lift(byte channel) => (byte)Math.Clamp(channel + (255 - channel) * 0.38, 0, 255);
            return Color.FromArgb(255, Lift(background.R), Lift(background.G), Lift(background.B));
        }

        private void LanguageService_LanguageChanged(object? sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                Localize();
                LoadEntries();
            });
        }

        private void LoadEntries()
        {
            if (!_ready) return;
            EntriesList.Items.Clear();
            UpdateButtons();
            try
            {
                string category = _selectedCategory;
                string query = SearchBox.Text.Trim();
                string localizedCategory = LanguageService.Translate(category);
                var entries = ProgramCategoryHistory.GetAll()
                    .Where(entry => ProgramCategoryHistory.Matches(entry, category, query, localizedCategory))
                    .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                foreach (var entry in entries)
                {
                    var details = new StackPanel { Spacing = 2 };
                    details.Children.Add(new TextBlock
                    {
                        Text = $"{entry.Name}  ·  {LanguageService.Translate(entry.Excluded ? "禁止自动分类" : "用户分类记录")}",
                        Foreground = _selectedTextBrush,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    });
                    details.Children.Add(new TextBlock { Text = entry.TargetPath, Foreground = _selectedTextBrush, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
                    if (!string.Equals(entry.Identity, entry.TargetPath, StringComparison.OrdinalIgnoreCase))
                        details.Children.Add(new TextBlock { Text = entry.Identity, Foreground = _selectedTextBrush, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 });
                    EntriesList.Items.Add(new ListViewItem { Tag = entry, Content = details });
                }
                StatusText.Text = LanguageService.TranslateFormat("共 {count} 项", ("{count}", entries.Count));
                UpdateButtons();
            }
            catch (Exception ex)
            {
                StatusText.Text = LanguageService.TranslateFormat("分类记录读取失败：{error}", ("{error}", ex.Message));
            }
        }

        private void ApplySelection(bool excluded, bool createExclusion)
        {
            try
            {
                var selected = EntriesList.SelectedItems.OfType<ListViewItem>()
                    .Select(item => item.Tag).OfType<ProgramCategoryEntry>()
                    .Where(entry => entry.Excluded == excluded).ToList();
                if (selected.Count == 0) return;
                ProgramCategoryHistory.Remove(selected, createExclusion);
                LoadEntries();
            }
            catch (Exception ex)
            {
                StatusText.Text = LanguageService.TranslateFormat("分类记录保存失败：{error}", ("{error}", ex.Message));
            }
        }

        private void UpdateButtons()
        {
            var selected = EntriesList.SelectedItems.OfType<ListViewItem>()
                .Select(item => item.Tag).OfType<ProgramCategoryEntry>().ToList();
            RemoveButton.IsEnabled = ExcludeButton.IsEnabled = selected.Any(entry => !entry.Excluded);
            UnblockButton.IsEnabled = selected.Any(entry => entry.Excluded);
        }

        private void CategoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_ready || sender is not ToggleButton button || button.Tag is not string category) return;
            bool changed = _selectedCategory != category;
            _selectedCategory = category;
            UpdateCategoryButtons();
            if (changed) LoadEntries();
        }

        private void UpdateCategoryButtons()
        {
            foreach (var button in new[] { OfficeCategoryButton, GamesCategoryButton, MusicCategoryButton })
            {
                bool selected = (string)button.Tag == _selectedCategory;
                button.IsChecked = selected;
                button.Background = selected ? _selectedBrush : _transparentBrush;
                button.BorderBrush = selected ? _selectedBorderBrush : _transparentBrush;
                button.Foreground = selected ? _selectedTextBrush : _normalTextBrush;
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => LoadEntries();
        private void ClearSearchButton_Click(object sender, RoutedEventArgs e) => SearchBox.Text = string.Empty;
        private void EntriesList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_ready) UpdateButtons(); }
        private void RemoveButton_Click(object sender, RoutedEventArgs e) => ApplySelection(false, false);
        private void ExcludeButton_Click(object sender, RoutedEventArgs e) => ApplySelection(false, true);
        private void UnblockButton_Click(object sender, RoutedEventArgs e) => ApplySelection(true, false);
        private void CloseButton_Click(object sender, RoutedEventArgs e) => WindowFadeController.CloseWithAnimation(this);

        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (_sized) return;
            _sized = true;
            double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
            AppWindow.Resize(new SizeInt32((int)(960 * scale), (int)(650 * scale)));
            ModalWindowHelper.CenterOnOwner(App.MainWindow, this);
            WindowFadeController.FadeAndScaleIn(this, RootGrid);
        }
    }
}

