using BlockBox.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BlockBox.Helpers
{
    public static class ContentKindHelper
    {
        private static readonly Dictionary<ContentKind, string[]> Map = new()
        {
            [ContentKind.Application] = new[] { ".exe", ".lnk", ".url", ".bat", ".cmd", ".msi" },
            [ContentKind.Image] = new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".tif", ".tiff" },
            [ContentKind.Video] = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v" },
            [ContentKind.Audio] = new[] { ".mp3", ".wav", ".flac", ".aac", ".m4a", ".ogg", ".wma" },
            [ContentKind.Document] = new[] { ".txt", ".pdf", ".md", ".rtf" },
            [ContentKind.Word] = new[] { ".doc", ".docx", ".docm" },
            [ContentKind.Excel] = new[] { ".xls", ".xlsx", ".xlsm", ".csv" },
            [ContentKind.PowerPoint] = new[] { ".ppt", ".pptx", ".pptm" },
            [ContentKind.Archive] = new[] { ".zip", ".rar", ".7z", ".tar", ".gz" },
            [ContentKind.Code] = new[] { ".cs", ".py", ".js", ".ts", ".java", ".cpp", ".h", ".html", ".css" },
        };
        private static readonly string[] ApplicationExtensions =
    { ".exe", ".lnk", ".url", ".bat", ".cmd", ".msi" };

        /// <summary>是否属于"应用程序 / 快捷方式"，不备份</summary>
        public static bool IsApplication(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;

            // ⭐ UWP 视为应用程序
            if (filePath.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
                return true;

            string ext = Path.GetExtension(filePath)?.ToLowerInvariant() ?? "";
            return ApplicationExtensions.Contains(ext);
        }
        /// <summary>路径是否符合该类型</summary>
        public static bool Matches(string filePath, ContentKind kind, string customExts)
        {
            if (kind == ContentKind.All) return true;
            // ⭐ UWP / Store 应用：TargetPath 以 shell:AppsFolder\ 开头
            if (filePath.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
            {
                return kind == ContentKind.Application;
            }
            string ext = Path.GetExtension(filePath)?.ToLowerInvariant() ?? "";
            if (string.IsNullOrEmpty(ext)) return false;

            if (kind == ContentKind.Custom)
            {
                return ParseCustomExtensions(customExts).Contains(ext);
            }

            return Map.TryGetValue(kind, out var exts) && exts.Contains(ext);
        }

        /// <summary>UI 显示名</summary>
        public static string GetDisplayName(ContentKind kind) => kind switch
        {
            ContentKind.All => "所有文件",
            ContentKind.Application => "应用程序",
            ContentKind.Image => "图片",
            ContentKind.Video => "视频",
            ContentKind.Audio => "音频",
            ContentKind.Document => "文档",
            ContentKind.Word => "Word 文档",
            ContentKind.Excel => "Excel 表格",
            ContentKind.PowerPoint => "PPT 演示",
            ContentKind.Archive => "压缩包",
            ContentKind.Code => "代码文件",
            ContentKind.Custom => "自定义类型",
            _ => "未知"
        };

        /// <summary>
        /// 给 FileOpenPicker 用的扩展名列表（带点，比如 ".png"）。
        /// All 或无法识别时返回 ["*"]。
        /// </summary>
        public static List<string> GetFilePickerExtensions(ContentKind kind, string customExts)
        {
            if (kind == ContentKind.All)
                return new List<string> { "*" };

            if (kind == ContentKind.Custom)
            {
                var list = ParseCustomExtensions(customExts);

                return list.Count > 0 ? list : new List<string> { "*" };
            }

            if (Map.TryGetValue(kind, out var exts))
                return exts.ToList();

            return new List<string> { "*" };
        }

        private static List<string> ParseCustomExtensions(string? customExts)
        {
            if (string.IsNullOrWhiteSpace(customExts)) return new List<string>();

            return customExts.Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim().ToLowerInvariant())
                .Where(value => value.Length > 0)
                .Select(value => value.StartsWith('.') ? value : "." + value)
                .Where(value => value.Length > 1 && value.IndexOf('.', 1) < 0
                    && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
                    && !value.Any(char.IsWhiteSpace))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// 获取某类型对应的扩展名列表（带点）。
        /// All 或 Custom 返回空列表。
        /// </summary>
        public static List<string> GetExtensions(ContentKind kind)
        {
            if (kind == ContentKind.All || kind == ContentKind.Custom)
                return new List<string>();

            return Map.TryGetValue(kind, out var exts)
                ? exts.ToList()
                : new List<string>();
        }

        /// <summary>
        /// ComboBox 显示用：名字 + 后缀，比如 "图片  .png .jpg .jpeg .gif .bmp .webp .ico .tiff"
        /// </summary>
        public static string GetDisplayNameWithExtensions(ContentKind kind)
        {
            string name = LanguageService.Translate(GetDisplayName(kind));

            if (kind == ContentKind.All || kind == ContentKind.Custom)
                return name;

            var exts = GetExtensions(kind);
            if (exts.Count == 0) return name;

            return $"{name}  {string.Join(" ", exts)}";
        }


    }
}
