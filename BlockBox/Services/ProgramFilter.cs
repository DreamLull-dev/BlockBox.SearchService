using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BlockBox.Services
{
    public sealed class ProgramFilter
    {
        // ==================== 过滤词 ====================
        private readonly List<string> _contains = new();
        private readonly List<string> _startsWith = new();
        private readonly List<string> _endsWith = new();
        private readonly List<string> _whitelist = new();

        // ==================== 分类词（公开给分类器用）====================
        public List<string> GameKeywords { get; } = new();
        public List<string> OfficeKeywords { get; } = new();
        public List<string> MusicKeywords { get; } = new();

        // ==================== 内置默认分类词 ====================
        private static readonly string[] DefaultGameKeywords =
        {
            "steam", "epic games", "gog", "ea app", "ea desktop", "uplay", "ubisoft",
            "battle.net", "blizzard", "riot", "league of legends", "英雄联盟", "valorant", "无畏契约",
            "minecraft", "roblox", "garena", "wegame", "xbox game bar", "游戏",
            "原神", "genshin impact", "genshin", "崩坏", "绝区零", "王者荣耀", "和平精英",
            "影之诗", "云顶之弈", "teamfight tactics", "dota", "counter-strike", "cs2", "csgo",
            "fortnite", "堡垒之夜", "pubg", "apex legends", "守望先锋", "overwatch",
            "炉石传说", "hearthstone", "魔兽世界", "world of warcraft", "暗黑破坏神", "diablo",
            "path of exile", "流放之路", "warframe", "命运2", "destiny 2", "逃离塔科夫", "escape from tarkov",
            "三角洲行动", "delta force", "穿越火线", "crossfire", "逆战", "地下城与勇士", "dungeon fighter online",
            "剑网3", "逆水寒", "天涯明月刀", "梦幻西游", "大话西游", "坦克世界", "world of tanks",
            "战争雷霆", "war thunder", "黎明杀机", "dead by daylight", "漫威争锋", "marvel rivals",
            "七日世界", "once human", "the finals", "绝地求生",
            "grand theft auto", "gta v", "gta vi", "侠盗猎车手", "red dead redemption", "荒野大镖客",
            "cyberpunk 2077", "赛博朋克2077", "the witcher", "巫师3", "elden ring", "艾尔登法环",
            "dark souls", "黑暗之魂", "sekiro", "只狼", "lies of p", "匹诺曹的谎言",
            "baldur's gate 3", "博德之门3", "hogwarts legacy", "霍格沃茨之遗", "starfield", "星空",
            "assassin's creed", "刺客信条", "far cry", "孤岛惊魂", "rainbow six", "彩虹六号",
            "call of duty", "使命召唤", "battlefield", "战地", "resident evil", "生化危机",
            "ea sports fc", "fifa",
            "final fantasy", "最终幻想", "monster hunter", "怪物猎人", "forza horizon", "极限竞速",
            "halo", "光环", "doom", "地铁离去", "metro exodus", "fallout", "辐射",
            "god of war", "战神", "spider-man", "漫威蜘蛛侠", "horizon zero dawn", "地平线：零之曙光",
            "the last of us", "最后生还者", "uncharted", "神秘海域", "death stranding", "死亡搁浅",
            "黑神话：悟空", "black myth: wukong", "鸣潮", "wuthering waves", "崩坏：星穹铁道",
            "honkai: star rail", "崩坏3", "honkai impact 3rd", "永劫无间", "naraka: bladepoint"
        };

        private static readonly string[] DefaultOfficeKeywords =
        {
            "microsoft 365", "microsoft office", "office", "microsoft word", "excel", "powerpoint", "outlook", "onenote",
            "wps", "notion", "obsidian", "typora", "evernote", "joplin",
            "libreoffice", "openoffice", "acrobat", "foxit", "pdf",
            "onedrive", "dropbox", "google drive", "google docs", "google sheets", "google slides",
            "xmind", "mindmaster", "visio", "microsoft project", "microsoft access",
            "microsoft teams", "sharepoint", "zoom workplace", "slack", "trello", "asana", "todoist",
            "clickup", "办公", "文档", "表格"
        };

        private static readonly string[] DefaultMusicKeywords =
        {
            "musicbee", "spotify", "foobar2000", "foobar", "itunes", "apple music", "aimp", "winamp", "千千静听",
            "网易云音乐", "qq音乐", "酷狗音乐", "酷我音乐", "虾米音乐", "咪咕音乐",
            "audacity", "cubase", "fl studio", "studio one", "ableton",
            "logic pro", "reaper", "cakewalk", "music", "音乐", "播放器"
        };

        // ==================== 加载 ====================

        public static ProgramFilter LoadOrCreateDefault(string iniPath)
        {
            try
            {
                if (!File.Exists(iniPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(iniPath)!);
                    File.WriteAllText(iniPath, DefaultIniContent);
                }
            }
            catch { }

            return Load(iniPath);
        }

        public static ProgramFilter Load(string iniPath)
        {
            var filter = new ProgramFilter();

            // ⭐ 先把内置默认分类词填进去
            filter.GameKeywords.AddRange(DefaultGameKeywords);
            filter.OfficeKeywords.AddRange(DefaultOfficeKeywords);
            filter.MusicKeywords.AddRange(DefaultMusicKeywords);

            if (!File.Exists(iniPath))
                return Normalize(filter);

            string currentSection = "";
            foreach (var raw in File.ReadAllLines(iniPath))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith(";") || line.StartsWith("#")) continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    currentSection = line[1..^1].Trim();
                    continue;
                }

                string word = line.ToLowerInvariant();
                switch (currentSection)
                {
                    case "BlockContains": filter._contains.Add(word); break;
                    case "BlockStartsWith": filter._startsWith.Add(word); break;
                    case "BlockEndsWith": filter._endsWith.Add(word); break;
                    case "Whitelist": filter._whitelist.Add(word); break;
                    // ⭐ 分类词：追加到默认词后面
                    case "GameKeywords": filter.GameKeywords.Add(word); break;
                    case "OfficeKeywords": filter.OfficeKeywords.Add(word); break;
                    case "MusicKeywords": filter.MusicKeywords.Add(word); break;
                }
            }

            return Normalize(filter);
        }

        private static ProgramFilter Normalize(ProgramFilter f)
        {
            Deduplicate(f.GameKeywords);
            Deduplicate(f.OfficeKeywords);
            Deduplicate(f.MusicKeywords);
            Deduplicate(f._contains);
            Deduplicate(f._startsWith);
            Deduplicate(f._endsWith);
            Deduplicate(f._whitelist);
            return f;
        }

        private static void Deduplicate(List<string> list)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (!seen.Add(list[i]))
                    list.RemoveAt(i);
            }
        }

        // ==================== 过滤判断 ====================

        public bool ShouldExclude(string? displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return false;

            string lower = displayName.ToLowerInvariant();

            foreach (var w in _whitelist)
                if (lower.Contains(w, StringComparison.Ordinal)) return false;

            foreach (var k in _contains)
                if (lower.Contains(k, StringComparison.Ordinal)) return true;

            foreach (var k in _startsWith)
                if (lower.StartsWith(k, StringComparison.Ordinal)) return true;

            foreach (var k in _endsWith)
                if (lower.EndsWith(k, StringComparison.Ordinal)) return true;

            return false;
        }

        // ==================== 默认 INI 内容 ====================

        private const string DefaultIniContent =
@"; ============================================================
; 程序导入过滤器 + 分类关键词
; 每行一个词，忽略空行和以 ; 或 # 开头的注释
; 修改后点按钮时会自动重新加载，不用重启
;
; 匹配规则：
;   [BlockContains]   名字包含该词 → 过滤
;   [BlockStartsWith] 名字以该词开头 → 过滤
;   [BlockEndsWith]   名字以该词结尾 → 过滤
;   [Whitelist]       名字包含该词 → 强制保留（优先级最高）
;
; 分类词（追加到内置默认词后面）：
;   [GameKeywords]    命中 → 归入「游戏」
;   [OfficeKeywords]  命中 → 归入「办公」
;   [MusicKeywords]   命中 → 归入「音乐」
;   优先级：游戏 > 办公 > 音乐
; ============================================================

[BlockContains]
卸载记录
readme
帮助文档
hotfix
update for
卸载
移除
Error
语言包
程序包
Remove
msiexec
Patcher
lghub_system_tray
010 Editor Homepage
010 Editor Support Online
官网
UltraISO 在线订购
UltraISO 主页
StreamFX (for OBS Studio) on the Web
Steam Support Center
Substance Painter Online Documentation
Creative Cloud Uninstaller

[BlockStartsWith]
unins
Nsight
关于
网站

[BlockEndsWith]
更新程序
补丁
Help File
Updater
Reporter
首选项
日志
Uninst
Patcher
下载

[Whitelist]
ddu
wagnardsoft

[GameKeywords]
; 这里可以补充额外的游戏关键词
; 比如：某某游戏启动器

[OfficeKeywords]
; 这里可以补充额外的办公软件关键词

[MusicKeywords]
; 这里可以补充额外的音乐软件关键词
";
    }
}