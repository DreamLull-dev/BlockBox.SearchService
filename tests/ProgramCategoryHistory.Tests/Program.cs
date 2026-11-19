using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlockBox.Models;
using BlockBox.Services;

string root = Path.Combine(Path.GetTempPath(), "BlockBox-category-tests-" + Guid.NewGuid());
BlockBox.Helpers.AppDataPaths.OverrideRoot = root;
try
{
    var office = new Category { IsShared = true, Name = "办公" };
    var music = new Category { IsShared = true, Name = "音乐" };
    var program = new ShortcutItem { Name = "Example", TargetPath = Path.Combine(root, "Example.exe") };
    ProgramCategoryHistory.Remember(program, office);
    ProgramCategoryHistory.Remember(program, music);
    ProgramCategoryHistory.Remember(program, office);
    var entries = ProgramCategoryHistory.GetAll();
    Check(entries.Count == 2 && entries.All(e => !e.Excluded), "remember and deduplicate categories");
    Check(entries.Count(e => ProgramCategoryHistory.Matches(e, "办公", "", "Office")) == 1, "show only selected category");
    Check(entries.Count(e => ProgramCategoryHistory.Matches(e, "游戏", "Example", "Games")) == 0, "search stays within selected category");
    Check(entries.Count(e => ProgramCategoryHistory.Matches(e, "办公", "office", "Office")) == 1, "search localized category case-insensitively");
    Check(entries.Count(e => ProgramCategoryHistory.Matches(e, "办公", "EXAMPLE.EXE", "Office")) == 1, "search target path case-insensitively");
    Check(File.Exists(Path.Combine(root, "setting", "program-category-history.json")), "persist outside database");
    Check(ProgramCategoryHistory.GetIdentity(program.TargetPath) == entries[0].Identity, "match executable identity");
    Check(ProgramCategoryHistory.GetIdentity("shell:AppsFolder\\Example!App") == "shell:AppsFolder\\Example!App", "match store identity");
    Check(ProgramCategoryHistory.GetIdentity("not-a-program.txt") == null, "reject non-program identity");
    ProgramCategoryHistory.Remove(entries.Where(e => e.Category == "办公"), true);
    entries = ProgramCategoryHistory.GetAll();
    Check(entries.Single(e => e.Category == "办公").Excluded && !entries.Single(e => e.Category == "音乐").Excluded, "exclude one category only");
    ProgramCategoryHistory.Remember(program, office);
    entries = ProgramCategoryHistory.GetAll();
    Check(!entries.Single(e => e.Category == "办公").Excluded, "manual add clears exclusion");
    ProgramCategoryHistory.Remove(entries.Where(e => e.Category == "办公"), false);
    Check(ProgramCategoryHistory.GetAll().Count == 1, "remove history only");
    var game = new Category { IsShared = true, Name = "游戏" };
    ProgramCategoryHistory.Remember(program, office);
    ProgramCategoryHistory.Remember(program, game, office);
    entries = ProgramCategoryHistory.GetAll();
    Check(entries.Count == 2 && entries.Any(e => e.Category == "音乐")
        && entries.Any(e => e.Category == "游戏") && entries.All(e => e.Category != "办公"),
        "move removes source history but preserves other categories");
    ProgramCategoryHistory.Remember(program, office);
    entries = ProgramCategoryHistory.GetAll();
    ProgramCategoryHistory.Remove(entries.Where(e => e.Category == "办公"), true);
    ProgramCategoryHistory.Remember(program, music, game);
    entries = ProgramCategoryHistory.GetAll();
    Check(entries.Any(e => e.Category == "办公" && e.Excluded)
        && entries.Any(e => e.Category == "音乐" && !e.Excluded)
        && entries.All(e => e.Category != "游戏"), "move preserves unrelated exclusions");
    ProgramCategoryHistory.Remember(program, new Category { Name = "音乐", IsShared = false }, music);
    Check(ProgramCategoryHistory.GetAll().All(e => e.Category != "音乐"), "move to non-default category removes source history");
    var filter = new ProgramFilter();
    filter.GameKeywords.Add("example");
    filter.OfficeKeywords.Add("example");
    var scanned = new InstalledProgramInfo { DisplayName = "Example" };
    Check(ProgramCategoryClassifier.Classify(scanned, filter) == ProgramCategoryClassifier.Category.Game, "default classification priority");
    Check(ProgramCategoryClassifier.Classify(scanned, filter, new HashSet<ProgramCategoryClassifier.Category> { ProgramCategoryClassifier.Category.Game }) == ProgramCategoryClassifier.Category.Office, "excluded classification falls through");
    File.WriteAllText(Path.Combine(root, "setting", "program-category-history.json"), "not json");
    bool failed = false;
    try { ProgramCategoryHistory.Remember(program, office); }
    catch (System.Text.Json.JsonException) { failed = true; }
    Check(failed, "do not overwrite corrupt history");
    Console.WriteLine("All category history checks passed.");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, true);
}

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception("Failed: " + name);
}

namespace Windows.Storage
{
    public sealed class ApplicationData
    {
        public static ApplicationData Current { get; } = new();
        public Folder LocalFolder { get; } = new();
    }
    public sealed class Folder { public string Path { get; set; } = ""; }
}
namespace BlockBox.Data
{
    public static class ExcludedProgramRepository
    {
        public static string? GetExecutablePath(string? path)
            => !string.IsNullOrWhiteSpace(path) && Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFullPath(path) : null;
    }
}
namespace BlockBox.Models
{
    public sealed class Category { public bool IsShared { get; set; } public string Name { get; set; } = ""; }
    public sealed class ShortcutItem { public string Name { get; set; } = ""; public string TargetPath { get; set; } = ""; }
    public sealed class InstalledProgramInfo { public string DisplayName { get; set; } = ""; public string? Publisher { get; set; } }
}
namespace BlockBox.Services
{
    public sealed class ProgramFilter
    {
        public List<string> GameKeywords { get; } = new();
        public List<string> OfficeKeywords { get; } = new();
        public List<string> MusicKeywords { get; } = new();
    }
}
