using System.IO;

namespace BlockBox.Helpers
{
    public static class FileIconHelper
    {
        /// <summary>
        /// 根据文件路径返回 Segoe Fluent Icons 的 Glyph。
        /// </summary>
        public static string GetGlyphForFile(string filePath)
        {
            string ext = Path.GetExtension(filePath)?.ToLowerInvariant() ?? "";

            return ext switch
            {
                // 图片
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp"
                    or ".webp" or ".ico" or ".tiff"
                    => "\uEB9F",

                // 视频
                ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".flv"
                    => "\uE714",

                // 音频
                ".mp3" or ".wav" or ".flac" or ".aac" or ".m4a" or ".ogg"
                    => "\uE8D6",

                // 纯文本 / 文档
                ".txt" or ".md" or ".rtf"
                    => "\uE8A5",
                ".pdf" => "\uEA90",
                ".doc" or ".docx" or ".docm" => "\uE8A5",
                ".xls" or ".xlsx" or ".xlsm" or ".csv" => "\uE9F9",
                ".ppt" or ".pptx" or ".pptm" => "\uE786",

                // 压缩包
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz"
                    => "\uF012",

                // 应用程序 / 快捷方式
                ".exe" or ".msi" => "\uE7C3",
                ".lnk" or ".url" => "\uE71B",
                ".bat" or ".cmd" => "\uE756",

                // 代码
                ".cs" or ".py" or ".js" or ".ts" or ".java"
                    or ".cpp" or ".h" or ".csproj"
                    => "\uE943",
                ".html" or ".css" or ".xml" or ".json"
                    => "\uE774",

                // 默认
                _ => "\uE7C3"
            };
        }
    }
}
