using BlockBox.Helpers;
using BlockBox.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BlockBox
{
    public sealed partial class MainWindow
    {
        /// <summary>
        /// 读一次系统剪贴板，把里面的文件路径缓存起来
        /// </summary>
        private Task RefreshSystemClipboardCacheAsync()
        {
            _systemClipboardPathsCache.Clear();

            try
            {
                // 剪贴板序列号变了，说明外部程序改过剪贴板，内部缓存失效
                uint currentSeq = ClipboardHelper.GetClipboardSequenceNumberSafe();
                if (currentSeq != _lastClipboardSeq)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[剪贴板] 外部变化 seq {_lastClipboardSeq} → {currentSeq}，清空内部缓存");

                    _internalClipboard.Clear();
                    _internalClipboardIsCut = false;
                    _lastClipboardSeq = currentSeq;
                }

                var paths = ClipboardHelper.GetFileDropList();
                if (paths.Count > 0)
                    _systemClipboardPathsCache.AddRange(paths);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[剪贴板] 读取失败: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        private string? BuildPasteMenuText()
        {
            if (_internalClipboard.Count > 0)
            {
                if (_internalClipboard.Count == 1)
                    return LanguageService.TranslateFormat("粘贴 {item}", ("{item}", ShortenName(_internalClipboard[0].Name)));
                return LanguageService.TranslateFormat("粘贴 {count} 项", ("{count}", _internalClipboard.Count));
            }

            if (_systemClipboardPathsCache.Count > 0)
            {
                if (_systemClipboardPathsCache.Count == 1)
                    return LanguageService.TranslateFormat("粘贴 {item}", ("{item}", ShortenName(Path.GetFileName(_systemClipboardPathsCache[0]))));
                return LanguageService.TranslateFormat("粘贴 {count} 个文件", ("{count}", _systemClipboardPathsCache.Count));
            }

            return null;
        }

        private async Task PasteAsync()
        {
            if (IsManualFileAddingDisabled())
            {
                await ShowMessageAsync(LanguageService.Translate("当前页面不支持手动添加文件。"));
                return;
            }

            if (_internalClipboard.Count > 0)
            {
                await PasteInternalAsync();
                return;
            }

            if (_systemClipboardPathsCache.Count > 0)
            {
                if (NavMenuList.SelectedItem is not Category cat || cat.IsSeparator)
                {
                    await ShowMessageAsync(LanguageService.Translate("请先选择一个分类"));
                    return;
                }

                if (!string.IsNullOrEmpty(_searchKeyword))
                {
                    await ShowMessageAsync(LanguageService.Translate("搜索状态下不能粘贴，请先清空搜索"));
                    return;
                }

                await BatchAddShortcutsAsync(_systemClipboardPathsCache.ToList(), cat);
                return;
            }

            await ShowMessageAsync(LanguageService.Translate("剪贴板里没有可粘贴的文件"));
        }

        private static string ShortenName(string? name, int max = 14)
        {
            if (string.IsNullOrEmpty(name)) return LanguageService.Translate("文件");
            return name.Length <= max ? name : name.Substring(0, max) + "…";
        }
    }
}
