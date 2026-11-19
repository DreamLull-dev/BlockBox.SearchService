using BlockBox.Models;
using System.Collections.Generic;

namespace BlockBox.Services
{
    public static class ProgramCategoryClassifier
    {
        public enum Category
        {
            AllApps,
            Game,
            Office,
            Music
        }

        /// <summary>
        /// 分类。关键词由 ProgramFilter 提供（内置默认 + INI 补充）。
        /// </summary>
        public static Category Classify(InstalledProgramInfo program, ProgramFilter filter, ISet<Category>? excluded = null)
        {
            string text = $"{program.DisplayName} {program.Publisher}".ToLowerInvariant();

            if (excluded?.Contains(Category.Game) != true && ContainsAny(text, filter.GameKeywords)) return Category.Game;
            if (excluded?.Contains(Category.Office) != true && ContainsAny(text, filter.OfficeKeywords)) return Category.Office;
            if (excluded?.Contains(Category.Music) != true && ContainsAny(text, filter.MusicKeywords)) return Category.Music;
            return Category.AllApps;
        }

        private static bool ContainsAny(string text, System.Collections.Generic.List<string> keywords)
        {
            foreach (var kw in keywords)
                if (text.Contains(kw, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}