namespace McLauncher.Services;

public enum OptionKind { Number, Toggle, Choice, Text, Keybind }

/// 一条可编辑的游戏选项定义(label = 给用户看的名字;Aliases = 模糊匹配用的别名)
public sealed class OptionDef
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public OptionKind Kind { get; init; } = OptionKind.Number;
    public double Min { get; init; }
    public double Max { get; init; } = 1;
    public double Step { get; init; } = 0.05;
    public string[]? Choices { get; init; }
    public string[] Aliases { get; init; } = [];
    public string Hint { get; init; } = "";
    /// 旧版本可能用的替代键名(找不到主键时回退)
    public string[] FallbackKeys { get; init; } = [];
}

/// 游戏选项目录 + 模糊匹配 + 常用语言 + 键位名称/旧版键码映射。
public static class OptionCatalog
{
    public static readonly IReadOnlyList<OptionDef> Options = new List<OptionDef>
    {
        new() { Key = "lang", Label = "语言", Kind = OptionKind.Choice, Choices = LanguageNames.Map.Keys.ToArray(),
                Aliases = ["语言", "language", "中文", "lang"] },
        new() { Key = "soundCategory_master", Label = "主音量", Min = 0, Max = 1, Aliases = ["主音量", "音量", "volume", "声音"] },
        new() { Key = "soundCategory_music", Label = "音乐", Min = 0, Max = 1, Aliases = ["音乐", "music", "bgm", "背景音乐"] },
        new() { Key = "soundCategory_record", Label = "唱片机 / 音符盒", Min = 0, Max = 1, Aliases = ["唱片", "音符盒", "jukebox", "record"] },
        new() { Key = "soundCategory_weather", Label = "天气音效", Min = 0, Max = 1, Aliases = ["天气", "weather", "雨声"] },
        new() { Key = "soundCategory_blocks", Label = "方块音效", Min = 0, Max = 1, Aliases = ["方块", "blocks"] },
        new() { Key = "soundCategory_hostile", Label = "敌对生物", Min = 0, Max = 1, Aliases = ["敌对", "怪物", "hostile"] },
        new() { Key = "soundCategory_neutral", Label = "中立生物", Min = 0, Max = 1, Aliases = ["中立", "neutral"] },
        new() { Key = "soundCategory_players", Label = "玩家音效", Min = 0, Max = 1, Aliases = ["玩家", "players"] },
        new() { Key = "soundCategory_ambient", Label = "环境音效", Min = 0, Max = 1, Aliases = ["环境", "ambient"] },
        new() { Key = "soundCategory_voice", Label = "语音", Min = 0, Max = 1, Aliases = ["语音", "voice"] },

        new() { Key = "renderDistance", Label = "视距", Min = 2, Max = 32, Step = 1,
                Aliases = ["视距", "渲染距离", "距离", "render", "distance", "rd"] },
        new() { Key = "simulationDistance", Label = "模拟距离", Min = 3, Max = 32, Step = 1,
                Aliases = ["模拟距离", "模拟", "simulation", "sim"], Hint = "1.18+ 才有" },
        new() { Key = "fov", Label = "视野", Min = 30, Max = 110, Step = 1, Aliases = ["视野", "fov", "视角"] },
        new() { Key = "gamma", Label = "亮度", Min = 0, Max = 1, Aliases = ["亮度", "brightness", "gamma"] },
        new() { Key = "mouseSensitivity", Label = "鼠标灵敏度", Min = 0, Max = 1, Step = 0.01, Aliases = ["灵敏度", "鼠标", "sensitivity"] },
        new() { Key = "guiScale", Label = "界面尺寸", Min = 0, Max = 4, Step = 1, Hint = "0 = 自动", Aliases = ["界面", "缩放", "gui", "scale"] },
        new() { Key = "graphicsMode", FallbackKeys = ["graphics"], Label = "图形", Kind = OptionKind.Choice,
                Choices = ["fancy", "fast", "fabulous"], Aliases = ["图形", "画质", "graphics"] },
        new() { Key = "particles", Label = "粒子效果", Kind = OptionKind.Choice,
                Choices = ["all", "decreased", "minimal"], Aliases = ["粒子", "particles"] },
        new() { Key = "renderClouds", Label = "云", Kind = OptionKind.Choice,
                Choices = ["true", "false", "fast"], Aliases = ["云", "clouds", "云层"] },
        new() { Key = "enableVsync", Label = "垂直同步", Kind = OptionKind.Toggle, Aliases = ["垂直同步", "vsync"] },
        new() { Key = "bobView", Label = "视角摇晃", Kind = OptionKind.Toggle, Aliases = ["摇晃", "bob", "视差"] },
        new() { Key = "autoJump", Label = "自动跳跃", Kind = OptionKind.Toggle, Aliases = ["自动跳跃", "autojump"] },
        new() { Key = "chatVisibility", Label = "聊天可见性", Kind = OptionKind.Choice,
                Choices = ["full", "system", "hidden"], Aliases = ["聊天", "chat"] },
    };

    /// 常用语言(显示名 → 代码)与反查
    public static class LanguageNames
    {
        public static readonly Dictionary<string, string> Map = new()
        {
            ["简体中文"] = "zh_CN",
            ["繁體中文"] = "zh_TW",
            ["English"] = "en_us",
            ["日本語"] = "ja_jp",
            ["한국어"] = "ko_kr",
            ["Русский"] = "ru_ru",
            ["Deutsch"] = "de_de",
            ["Français"] = "fr_fr",
            ["Español"] = "es_es",
            ["Português (BR)"] = "pt_br",
            ["Italiano"] = "it_it",
            ["Polski"] = "pl_pl",
        };
        public static string CodeToName(string code) =>
            Map.FirstOrDefault(p => string.Equals(p.Value, code, StringComparison.OrdinalIgnoreCase)).Key ?? code;
    }

    /// 键位动作 → 中文名(常用集;查不到的显示原始动作名)
    public static readonly Dictionary<string, string> KeybindNames = new()
    {
        ["forward"] = "前进", ["back"] = "后退", ["left"] = "向左", ["right"] = "向右",
        ["jump"] = "跳跃", ["sneak"] = "潜行", ["sprint"] = "疾跑", ["attack"] = "攻击 / 破坏",
        ["use"] = "使用 / 放置", ["pickItem"] = "选取方块", ["drop"] = "丢弃物品",
        ["inventory"] = "物品栏", ["swapOffhand"] = "交换副手", ["chat"] = "打开聊天",
        ["command"] = "输入指令", ["playerList"] = "玩家列表", ["socialInteractions"] = "社交屏幕",
        ["advancements"] = "进度", ["togglePerspective"] = "切换视角", ["fullscreen"] = "全屏",
        ["smoothCamera"] = "平滑视角", ["spectatorOutlines"] = "旁观者高亮", ["screenshot"] = "截图",
        ["saveToolbarActivator"] = "保存快捷栏", ["loadToolbarActivator"] = "载入快捷栏",
    };

    /// 选项目录的本地化标签键(显示走 Loc.T;搜索文本仍同时包含中英文)
    private static readonly Dictionary<string, string> LabelKeys = new()
    {
        ["lang"] = "S_OptLang",
        ["soundCategory_master"] = "S_OptMaster",
        ["soundCategory_music"] = "S_OptMusic",
        ["soundCategory_record"] = "S_OptRecord",
        ["soundCategory_weather"] = "S_OptWeather",
        ["soundCategory_blocks"] = "S_OptBlocks",
        ["soundCategory_hostile"] = "S_OptHostile",
        ["soundCategory_neutral"] = "S_OptNeutral",
        ["soundCategory_players"] = "S_OptPlayers",
        ["soundCategory_ambient"] = "S_OptAmbient",
        ["soundCategory_voice"] = "S_OptVoice",
        ["renderDistance"] = "S_OptRenderDistance",
        ["simulationDistance"] = "S_OptSimulationDistance",
        ["fov"] = "S_OptFov",
        ["gamma"] = "S_OptGamma",
        ["mouseSensitivity"] = "S_OptSensitivity",
        ["guiScale"] = "S_OptGuiScale",
        ["graphicsMode"] = "S_OptGraphics",
        ["particles"] = "S_OptParticles",
        ["renderClouds"] = "S_OptClouds",
        ["enableVsync"] = "S_OptVsync",
        ["bobView"] = "S_OptBob",
        ["autoJump"] = "S_OptAutoJump",
        ["chatVisibility"] = "S_OptChat",
    };

    /// 当前语言的显示标签
    public static string LabelFor(OptionDef def) =>
        LabelKeys.TryGetValue(def.Key, out var key) ? Loc.T(key) : def.Label;

    /// 模糊匹配:命中 label / key / 别名 / 键位名 任一即算(不分大小写、忽略空格)
    public static bool Matches(OptionDef def, string query)
    {
        var q = query.Trim().ToLowerInvariant().Replace(" ", "");
        if (q.Length == 0) return true;
        if (def.Label.ToLowerInvariant().Replace(" ", "").Contains(q)) return true;
        if (def.Key.ToLowerInvariant().Contains(q)) return true;
        foreach (var alias in def.Aliases)
            if (alias.ToLowerInvariant().Replace(" ", "").Contains(q)) return true;
        return false;
    }

    // ── 键位名称 ↔ 旧版数字键码(1.12 及以前用 LWJGL 键码)──
    private static readonly Dictionary<int, string> LegacyCodeToKey = new()
    {
        [1] = "escape", [14] = "backspace", [15] = "tab", [28] = "enter", [29] = "left.control",
        [42] = "left.shift", [54] = "right.shift", [56] = "left.alt", [57] = "space", [58] = "caps.lock",
        [69] = "num.lock", [70] = "scroll.lock", [199] = "home", [200] = "up", [201] = "page.up",
        [203] = "left", [205] = "right", [207] = "end", [208] = "down", [209] = "page.down",
        [210] = "insert", [211] = "delete",
    };
    private static readonly Dictionary<int, string> LegacyFunctionKeys =
        Enumerable.Range(0, 12).ToDictionary(i => 59 + i, i => $"f{i + 1}");

    /// 读到的键位值 → 展示名(如 key.keyboard.w → W;旧版 17 → W)
    public static string DisplayKey(string value)
    {
        if (int.TryParse(value, out var code)) return LegacyKeyName(code);
        if (value.StartsWith("key.keyboard.", StringComparison.OrdinalIgnoreCase))
            return value["key.keyboard.".Length..].ToUpperInvariant();
        if (value.StartsWith("key.mouse.", StringComparison.OrdinalIgnoreCase))
        {
            var button = value["key.mouse.".Length..];
            return "鼠标" + (button switch
            {
                "left" => "左键",
                "right" => "右键",
                "middle" => "中键",
                _ => button,
            });
        }
        return value;
    }

    private static string LegacyKeyName(int code)
    {
        if (code >= 65 && code <= 90) return ((char)code).ToString();
        if (code >= 48 && code <= 57) return ((char)code).ToString();
        if (LegacyFunctionKeys.TryGetValue(code, out var f)) return f.ToUpperInvariant();
        if (LegacyCodeToKey.TryGetValue(code, out var name)) return name.ToUpperInvariant();
        return $"#{code}";
    }

    /// 现代键名(WPF Key) → MC 键位值(旧格式则给数字键码;无法表示时返回 null)
    public static string? FromWpfKey(System.Windows.Input.Key key, bool legacyFormat)
    {
        var name = key switch
        {
            >= System.Windows.Input.Key.A and <= System.Windows.Input.Key.Z => key.ToString().ToLowerInvariant(),
            >= System.Windows.Input.Key.D0 and <= System.Windows.Input.Key.D9 => key.ToString()[1..],
            >= System.Windows.Input.Key.F1 and <= System.Windows.Input.Key.F12 => key.ToString().ToLowerInvariant(),
            System.Windows.Input.Key.Space => "space",
            System.Windows.Input.Key.LeftShift => "left.shift",
            System.Windows.Input.Key.RightShift => "right.shift",
            System.Windows.Input.Key.LeftCtrl => "left.control",
            System.Windows.Input.Key.RightCtrl => "right.control",
            System.Windows.Input.Key.LeftAlt => "left.alt",
            System.Windows.Input.Key.RightAlt => "right.alt",
            System.Windows.Input.Key.Tab => "tab",
            System.Windows.Input.Key.Escape => "escape",
            System.Windows.Input.Key.Enter => "enter",
            System.Windows.Input.Key.Back => "backspace",
            System.Windows.Input.Key.Up => "up",
            System.Windows.Input.Key.Down => "down",
            System.Windows.Input.Key.Left => "left",
            System.Windows.Input.Key.Right => "right",
            System.Windows.Input.Key.Home => "home",
            System.Windows.Input.Key.End => "end",
            System.Windows.Input.Key.PageUp => "page.up",
            System.Windows.Input.Key.PageDown => "page.down",
            System.Windows.Input.Key.Insert => "insert",
            System.Windows.Input.Key.Delete => "delete",
            _ => null,
        };
        if (name == null) return null;

        if (!legacyFormat) return "key.keyboard." + name;

        // 旧格式:只支持有对应数字键码的键
        var reverse = LegacyCodeToKey.Concat(LegacyFunctionKeys)
            .ToDictionary(p => p.Value, p => p.Key, StringComparer.OrdinalIgnoreCase);
        if (reverse.TryGetValue(name, out var code)) return code.ToString();
        if (name.Length == 1 && char.IsAsciiLetterUpper(name[0])) return ((int)char.ToUpperInvariant(name[0])).ToString();
        if (name.Length == 1 && char.IsAsciiDigit(name[0])) return ((int)name[0]).ToString();
        return null;
    }

    public static string MouseValue(int button, bool legacyFormat) => legacyFormat
        ? button switch { 0 => "0", 1 => "1", 2 => "2", _ => "" }
        : button switch { 0 => "key.mouse.left", 1 => "key.mouse.right", 2 => "key.mouse.middle", _ => $"key.mouse.{button + 1}" };
}
