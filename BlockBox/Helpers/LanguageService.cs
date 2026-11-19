using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;

namespace BlockBox.Helpers
{
    public static class LanguageService
    {
        private const string ChineseLanguageCode = "zh-CN";
        private const string DisplayNameKey = "_displayName";
        private static Dictionary<string, Dictionary<string, string>> Languages = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object ReloadLock = new();
        private static Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;
        private static readonly ConditionalWeakTable<DependencyObject, Dictionary<string, TrackedText>> TrackedTexts = new();
        private static readonly List<WeakReference<DependencyObject>> DynamicRoots = new();
        private static readonly List<WeakReference<MenuFlyout>> DynamicMenuFlyouts = new();
        private static string _currentLanguageCode = ChineseLanguageCode;
        private static FileSystemWatcher? _languageFileWatcher;
        private static Timer? _languageReloadTimer;
        private static string? _languageFilePath;
        private const double GeneratedButtonMinWidth = 88;
        private const double GeneratedButtonHeight = 32;
        private const double GeneratedButtonCornerRadius = 16;

        public static event EventHandler? LanguageChanged;

        private sealed class TrackedText
        {
            public string? Source { get; set; }
            public string? LastTranslation { get; set; }
        }

        public sealed class LanguageOption
        {
            public string Code { get; init; } = string.Empty;
            public string DisplayName { get; init; } = string.Empty;
        }

        public static string CurrentLanguageCode => _currentLanguageCode;

        public static void Configure(string languageFilePath)
        {
            try
            {
                _dispatcher ??= Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                _languageFilePath = languageFilePath;
                string? directory = Path.GetDirectoryName(languageFilePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                string bundled = Path.Combine(AppContext.BaseDirectory, "Assets", "language.json");
                var bundledLanguages = ReadLanguageFile(bundled);
                var userLanguages = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                bool canUpdateUserFile = true;

                if (File.Exists(languageFilePath))
                {
                    try
                    {
                        userLanguages = ReadLanguageFile(languageFilePath)
                            ?? throw new JsonException("Language file contains no language data.");
                    }
                    catch (Exception ex) when (ex is JsonException or IOException)
                    {
                        canUpdateUserFile = false;
                        System.Diagnostics.Debug.WriteLine($"[语言] 用户语言文件无法读取，使用内置语言包: {ex.Message}");
                    }
                }

                var allLanguages = bundledLanguages != null
                    ? bundledLanguages.ToDictionary(
                        pair => pair.Key,
                        pair => new Dictionary<string, string>(pair.Value, StringComparer.Ordinal),
                        StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

                foreach (var (code, userTranslations) in userLanguages)
                {
                    if (!allLanguages.TryGetValue(code, out var mergedTranslations))
                    {
                        mergedTranslations = new Dictionary<string, string>(StringComparer.Ordinal);
                        if (allLanguages.TryGetValue(ChineseLanguageCode, out var chineseDefaults))
                        {
                            foreach (var (key, value) in chineseDefaults)
                                mergedTranslations[key] = value;
                        }
                        allLanguages[code] = mergedTranslations;
                    }

                    foreach (var (key, value) in userTranslations)
                        mergedTranslations[key] = value;
                }

                if (allLanguages.Count == 0)
                    throw new InvalidDataException("No valid language pack could be loaded.");

                if (canUpdateUserFile)
                {
                    try
                    {
                        string json = JsonSerializer.Serialize(allLanguages, new JsonSerializerOptions { WriteIndented = true });
                        if (!File.Exists(languageFilePath)
                            || !string.Equals(File.ReadAllText(languageFilePath), json, StringComparison.Ordinal))
                        {
                            File.WriteAllText(languageFilePath, json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        System.Diagnostics.Debug.WriteLine($"[语言] 无法保存合并后的语言文件: {ex.Message}");
                    }
                }

                var loadedLanguages = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var (code, translations) in allLanguages)
                {
                    if (!string.IsNullOrWhiteSpace(code) && translations != null)
                        loadedLanguages[code] = new Dictionary<string, string>(translations, StringComparer.Ordinal);
                }

                if (!loadedLanguages.ContainsKey(ChineseLanguageCode))
                    loadedLanguages[ChineseLanguageCode] = new Dictionary<string, string>(StringComparer.Ordinal);
                if (!loadedLanguages.ContainsKey("en-US"))
                    loadedLanguages["en-US"] = new Dictionary<string, string>(StringComparer.Ordinal);

                if (loadedLanguages.TryGetValue(ChineseLanguageCode, out var defaultChineseTranslations))
                {
                    foreach (var translations in loadedLanguages.Values)
                    {
                        foreach (var (key, value) in defaultChineseTranslations)
                            translations.TryAdd(key, value);
                    }
                }

                loadedLanguages[ChineseLanguageCode].TryAdd(DisplayNameKey, "简体中文");
                loadedLanguages["en-US"].TryAdd(DisplayNameKey, "English");
                Languages = loadedLanguages;

                if (!Languages.ContainsKey(_currentLanguageCode))
                    _currentLanguageCode = ChineseLanguageCode;

                ConfigureLanguageFileWatcher(languageFilePath);
                LanguageChanged?.Invoke(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[语言] 配置加载失败: {ex.Message}");
                var fallbackLanguages = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                fallbackLanguages[ChineseLanguageCode] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [DisplayNameKey] = "简体中文"
                };
                fallbackLanguages["en-US"] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [DisplayNameKey] = "English"
                };
                Languages = fallbackLanguages;
            }
        }

        public static IReadOnlyList<LanguageOption> GetLanguages()
            => Languages
                .Select(pair => new LanguageOption
                {
                    Code = pair.Key,
                    DisplayName = pair.Value.TryGetValue(DisplayNameKey, out string? name) && !string.IsNullOrWhiteSpace(name)
                        ? name
                        : pair.Key
                })
                .OrderBy(option => option.Code == ChineseLanguageCode ? 0 : option.Code == "en-US" ? 1 : 2)
                .ThenBy(option => option.DisplayName, StringComparer.CurrentCulture)
                .ToList();

        public static void SetCurrentLanguage(string? languageCode)
        {
            string nextLanguageCode = !string.IsNullOrWhiteSpace(languageCode) && Languages.ContainsKey(languageCode)
                ? languageCode
                : ChineseLanguageCode;
            if (string.Equals(_currentLanguageCode, nextLanguageCode, StringComparison.OrdinalIgnoreCase))
                return;

            _currentLanguageCode = nextLanguageCode;
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        private static void ConfigureLanguageFileWatcher(string languageFilePath)
        {
            string? directory = Path.GetDirectoryName(languageFilePath);
            string fileName = Path.GetFileName(languageFilePath);
            if (string.IsNullOrEmpty(directory))
                return;

            if (_languageFileWatcher != null
                && string.Equals(_languageFileWatcher.Path, directory, StringComparison.OrdinalIgnoreCase)
                && string.Equals(_languageFileWatcher.Filter, fileName, StringComparison.OrdinalIgnoreCase))
                return;

            _languageFileWatcher?.Dispose();
            _languageFileWatcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };
            _languageFileWatcher.Changed += LanguageFileWatcher_Changed;
            _languageFileWatcher.Created += LanguageFileWatcher_Changed;
            _languageFileWatcher.Renamed += LanguageFileWatcher_Renamed;
            _languageFileWatcher.EnableRaisingEvents = true;
        }

        private static void LanguageFileWatcher_Changed(object sender, FileSystemEventArgs e)
            => ScheduleLanguageFileReload();

        private static void LanguageFileWatcher_Renamed(object sender, RenamedEventArgs e)
            => ScheduleLanguageFileReload();

        private static void ScheduleLanguageFileReload()
        {
            lock (ReloadLock)
            {
                _languageReloadTimer ??= new Timer(_ =>
                {
                    string? path = _languageFilePath;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        _dispatcher?.TryEnqueue(() => Configure(path));
                });
                _languageReloadTimer.Change(400, Timeout.Infinite);
            }
        }

        public static void ApplyLanguage(DependencyObject? root, string? languageCode)
        {
            SetCurrentLanguage(languageCode);
            string targetLanguageCode = _currentLanguageCode;
            Apply(root, targetLanguageCode);
            ApplyRegisteredDynamicContent(targetLanguageCode);
        }

        public static string Translate(string? value)
            => TranslateValue(value, _currentLanguageCode) ?? value ?? string.Empty;

        public static string TranslateFormat(string template, params (string Placeholder, object? Value)[] values)
        {
            string result = Translate(template);
            foreach (var (placeholder, value) in values)
                result = result.Replace(placeholder, Convert.ToString(value, System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty,
                    StringComparison.Ordinal);
            return result;
        }

        // Progress messages originate on worker threads; localize them on the UI thread.
        public static string TranslateProgress(string message)
        {
            var scan = System.Text.RegularExpressions.Regex.Match(message, @"^扫描到 (\d+) 个程序，正在分类导入\.\.\.$");
            if (scan.Success)
                return TranslateFormat("扫描到 {count} 个程序，正在分类导入...", ("{count}", scan.Groups[1].Value));

            var icons = System.Text.RegularExpressions.Regex.Match(message, @"^(正在准备程序图标|正在预载全部应用图标)（(\d+)/(\d+)）\.\.\.$");
            if (icons.Success)
            {
                string template = icons.Groups[1].Value == "正在准备程序图标"
                    ? "正在准备程序图标（{current}/{total}）..."
                    : "正在预载全部应用图标（{current}/{total}）...";
                return TranslateFormat(template, ("{current}", icons.Groups[2].Value), ("{total}", icons.Groups[3].Value));
            }
            return Translate(message);
        }

        public static void ApplyGeneratedButtonSize(Button button)
        {
            button.Width = double.NaN;
            button.MinWidth = GeneratedButtonMinWidth;
            button.Height = GeneratedButtonHeight;
            button.CornerRadius = new CornerRadius(GeneratedButtonCornerRadius);
        }

        public static void Apply(DependencyObject? root, string languageCode)
        {
            if (root == null) return;
            ApplyCore(root, languageCode, new HashSet<DependencyObject>());
        }

        private static void ApplyCore(DependencyObject root, string languageCode, HashSet<DependencyObject> visited)
        {
            if (!visited.Add(root))
                return;

            if (root is TextBox textBox)
                TextBoxCornerRadiusHelper.Attach(textBox);

            TranslateControl(root, languageCode);

            if (root is ContentControl { Content: DependencyObject content })
                ApplyCore(content, languageCode, visited);

            if (root is ItemsControl itemsControl)
            {
                foreach (object item in itemsControl.Items)
                {
                    if (item is DependencyObject itemElement)
                        ApplyCore(itemElement, languageCode, visited);
                }
            }

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
                ApplyCore(VisualTreeHelper.GetChild(root, i), languageCode, visited);
        }

        public static void ApplyMenuFlyout(MenuFlyout flyout, string languageCode)
        {
            foreach (var item in flyout.Items)
                TranslateMenuItem(item, languageCode);
        }

        public static void RegisterDynamicRoot(DependencyObject root)
        {
            DynamicRoots.Add(new WeakReference<DependencyObject>(root));
            Apply(root, _currentLanguageCode);
        }

        public static void RegisterMenuFlyout(MenuFlyout flyout)
        {
            DynamicMenuFlyouts.Add(new WeakReference<MenuFlyout>(flyout));
            ApplyMenuFlyout(flyout, _currentLanguageCode);
        }

        public static void ApplyRegisteredDynamicContent(string languageCode)
        {
            for (int i = DynamicRoots.Count - 1; i >= 0; i--)
            {
                if (DynamicRoots[i].TryGetTarget(out DependencyObject? root))
                    Apply(root, languageCode);
                else
                    DynamicRoots.RemoveAt(i);
            }

            for (int i = DynamicMenuFlyouts.Count - 1; i >= 0; i--)
            {
                if (DynamicMenuFlyouts[i].TryGetTarget(out MenuFlyout? flyout))
                    ApplyMenuFlyout(flyout, languageCode);
                else
                    DynamicMenuFlyouts.RemoveAt(i);
            }
        }

        public static void ApplyDialog(ContentDialog dialog, string languageCode)
        {
            dialog.Opened -= ApplyDialogButtonSizes;
            dialog.Opened += ApplyDialogButtonSizes;
            var dialogButtonStyle = CreateDialogButtonStyle();
            dialog.PrimaryButtonStyle = dialogButtonStyle;
            dialog.SecondaryButtonStyle = dialogButtonStyle;
            dialog.CloseButtonStyle = dialogButtonStyle;

            if (dialog.Title is string title)
                ApplyTrackedText(dialog, nameof(dialog.Title), title, languageCode, value => dialog.Title = value);
            else
                dialog.Title = TranslateObject(dialog.Title, languageCode);

            if (dialog.Content is string contentText)
                ApplyTrackedText(dialog, nameof(dialog.Content), contentText, languageCode, value => dialog.Content = value);
            else
                dialog.Content = TranslateObject(dialog.Content, languageCode);

            ApplyTrackedText(dialog, nameof(dialog.PrimaryButtonText), dialog.PrimaryButtonText, languageCode,
                value => dialog.PrimaryButtonText = value ?? string.Empty);
            ApplyTrackedText(dialog, nameof(dialog.SecondaryButtonText), dialog.SecondaryButtonText, languageCode,
                value => dialog.SecondaryButtonText = value ?? string.Empty);
            ApplyTrackedText(dialog, nameof(dialog.CloseButtonText), dialog.CloseButtonText, languageCode,
                value => dialog.CloseButtonText = value ?? string.Empty);

            if (dialog.Content is DependencyObject content)
                Apply(content, languageCode);
        }

        private static Style CreateDialogButtonStyle()
        {
            var style = new Style(typeof(Button));
            if (Application.Current.Resources.TryGetValue("DefaultButtonStyle", out object defaultStyle)
                && defaultStyle is Style baseStyle)
            {
                style.BasedOn = baseStyle;
            }

            style.Setters.Add(new Setter(FrameworkElement.WidthProperty, double.NaN));
            style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, GeneratedButtonMinWidth));
            style.Setters.Add(new Setter(FrameworkElement.HeightProperty, GeneratedButtonHeight));
            style.Setters.Add(new Setter(Control.CornerRadiusProperty,
                new CornerRadius(GeneratedButtonCornerRadius)));
            return style;
        }

        private static void ApplyDialogButtonSizes(
            ContentDialog dialog,
            ContentDialogOpenedEventArgs args)
        {
            dialog.DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () =>
                {
                    dialog.UpdateLayout();
                    ApplyGeneratedButtonSizes(dialog);
                });
        }

        private static void ApplyGeneratedButtonSizes(DependencyObject root)
        {
            if (root is Button button)
                ApplyGeneratedButtonSize(button);

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
                ApplyGeneratedButtonSizes(VisualTreeHelper.GetChild(root, i));
        }

        private static void TranslateMenuItem(MenuFlyoutItemBase item, string languageCode)
        {
            if (item is MenuFlyoutSubItem subItem)
            {
                ApplyTrackedText(subItem, nameof(subItem.Text), subItem.Text, languageCode,
                    value => subItem.Text = value ?? string.Empty);
                foreach (var child in subItem.Items)
                    TranslateMenuItem(child, languageCode);
            }
            else if (item is MenuFlyoutItem menuItem)
            {
                ApplyTrackedText(menuItem, nameof(menuItem.Text), menuItem.Text, languageCode,
                    value => menuItem.Text = value ?? string.Empty);
            }
        }

        private static void ApplyTrackedText(
            DependencyObject owner,
            string property,
            string? currentValue,
            string languageCode,
            Action<string?> setValue)
        {
            var properties = TrackedTexts.GetValue(owner, _ => new Dictionary<string, TrackedText>());
            if (!properties.TryGetValue(property, out TrackedText? tracked))
            {
                tracked = new TrackedText();
                properties[property] = tracked;
            }

            if (!string.Equals(currentValue, tracked.LastTranslation, StringComparison.Ordinal))
                tracked.Source = ResolveSourceKey(currentValue, languageCode) ?? currentValue;

            string? translated = TranslateValue(tracked.Source, languageCode);
            tracked.LastTranslation = translated;
            if (!string.Equals(currentValue, translated, StringComparison.Ordinal))
                setValue(translated);
        }

        private static string? TranslateValue(string? value, string languageCode)
        {
            if (string.IsNullOrEmpty(value) || !Languages.TryGetValue(languageCode, out var translations))
                return value;

            string? sourceKey = ResolveSourceKey(value, languageCode);

            if (sourceKey == null)
                return value;

            if (translations.TryGetValue(sourceKey, out string? translation) && !string.IsNullOrEmpty(translation))
                return translation;

            return Languages.TryGetValue(ChineseLanguageCode, out var fallback)
                && fallback.TryGetValue(sourceKey, out string? fallbackTranslation)
                ? fallbackTranslation
                : sourceKey;
        }

        private static string? ResolveSourceKey(string? value, string languageCode)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            if (Languages.TryGetValue(ChineseLanguageCode, out var chinese) && chinese.ContainsKey(value))
                return value;

            if (Languages.TryGetValue(languageCode, out var currentLanguage))
            {
                string? sourceKey = FindSourceKey(currentLanguage, value);
                if (sourceKey != null)
                    return sourceKey;
            }

            foreach (var translations in Languages.Values)
            {
                string? sourceKey = FindSourceKey(translations, value);
                if (sourceKey != null)
                    return sourceKey;
            }

            return null;
        }

        private static string? FindSourceKey(Dictionary<string, string>? translations, string value)
            => translations?.FirstOrDefault(pair =>
                pair.Key != DisplayNameKey && string.Equals(pair.Value, value, StringComparison.Ordinal)).Key;

        private static Dictionary<string, Dictionary<string, string>>? ReadLanguageFile(string path)
        {
            if (!File.Exists(path))
                return null;

            return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(path));
        }

        private static object? TranslateObject(object? value, string languageCode)
        {
            if (value is string text)
                return TranslateValue(text, languageCode);
            if (value is DependencyObject dependencyObject)
                Apply(dependencyObject, languageCode);
            return value;
        }

        private static void TranslateControl(DependencyObject element, string languageCode)
        {
            if (element is FrameworkElement { Tag: string tag } && tag == "SkipLocalization")
                return;

            if (element is TextBlock text)
                ApplyTrackedText(text, nameof(text.Text), text.Text, languageCode, value => text.Text = value ?? string.Empty);
            else if (element is Button button && button.Content is string content)
                ApplyTrackedText(button, nameof(button.Content), content, languageCode, value => button.Content = value);
            else if (element is MenuFlyoutSubItem subItem)
                ApplyTrackedText(subItem, nameof(subItem.Text), subItem.Text, languageCode, value => subItem.Text = value ?? string.Empty);
            else if (element is ListViewItem listItem && listItem.Content is string itemContent)
                ApplyTrackedText(listItem, nameof(listItem.Content), itemContent, languageCode, value => listItem.Content = value);
            else if (element is ComboBoxItem comboItem && comboItem.Content is string comboContent)
                ApplyTrackedText(comboItem, nameof(comboItem.Content), comboContent, languageCode, value => comboItem.Content = value);
            else if (element is MenuFlyoutItem menuItem)
                ApplyTrackedText(menuItem, nameof(menuItem.Text), menuItem.Text, languageCode, value => menuItem.Text = value ?? string.Empty);
            else if (element is TextBox textBox)
                ApplyTrackedText(textBox, nameof(textBox.PlaceholderText), textBox.PlaceholderText, languageCode,
                    value => textBox.PlaceholderText = value ?? string.Empty);
            else if (element is HyperlinkButton hyperlinkButton && hyperlinkButton.Content is string hyperlinkContent)
                ApplyTrackedText(hyperlinkButton, nameof(hyperlinkButton.Content), hyperlinkContent, languageCode,
                    value => hyperlinkButton.Content = value);
            else if (element is ContentControl contentControl && contentControl is not Button && contentControl.Content is string contentText)
                ApplyTrackedText(contentControl, nameof(contentControl.Content), contentText, languageCode,
                    value => contentControl.Content = value);

            if (element is FrameworkElement frameworkElement)
            {
                object? toolTip = ToolTipService.GetToolTip(frameworkElement);
                if (toolTip is string toolTipText)
                    ApplyTrackedText(frameworkElement, "ToolTip", toolTipText, languageCode,
                        value => ToolTipService.SetToolTip(frameworkElement, value));
            }

            if (element is ToggleSwitch toggleSwitch)
            {
                if (toggleSwitch.OnContent is string onContent)
                    ApplyTrackedText(toggleSwitch, nameof(toggleSwitch.OnContent), onContent, languageCode,
                        value => toggleSwitch.OnContent = value);
                if (toggleSwitch.OffContent is string offContent)
                    ApplyTrackedText(toggleSwitch, nameof(toggleSwitch.OffContent), offContent, languageCode,
                        value => toggleSwitch.OffContent = value);
            }

            if (element is ComboBox comboBox)
            {
                foreach (object item in comboBox.Items)
                {
                    if (item is DependencyObject itemElement)
                        Apply(itemElement, languageCode);
                }
            }
        }
    }
}
