using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using McLauncher.Services;

namespace McLauncher.ViewModels;

/// 一行可编辑选项(数值/开关/选择/文本/键位,按 Kind 显示对应编辑器)
public partial class SettingRow : ObservableObject
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    /// 供搜索的合并文本(label + 键名 + 别名 + 键位显示名;全小写)
    public string SearchText { get; init; } = "";
    public OptionKind Kind { get; init; } = OptionKind.Number;
    public double Min { get; init; }
    public double Max { get; init; } = 1;
    public double Step { get; init; } = 0.05;
    public string[]? Choices { get; init; }
    public string Hint { get; init; } = "";
    public bool Missing { get; init; }

    [ObservableProperty] private double _numberValue;

    partial void OnNumberValueChanged(double value) => OnPropertyChanged(nameof(DisplayValue));
    [ObservableProperty] private bool _boolValue;
    [ObservableProperty] private string _textValue = "";
    [ObservableProperty] private string _keyDisplay = "";
    [ObservableProperty] private bool _visible = true;

    public bool IsNumberEditor => !Missing && Kind == OptionKind.Number;
    public bool IsToggleEditor => !Missing && Kind == OptionKind.Toggle;
    public bool IsChoiceEditor => !Missing && Kind == OptionKind.Choice;
    public bool IsKeybindEditor => !Missing && Kind == OptionKind.Keybind;
    public bool IsMissing => Missing;
    public string MissingText => Missing ? Loc.T("S_GS_Missing") : "";

    public string DisplayValue => Kind == OptionKind.Number
        ? (NumberValue == Math.Floor(NumberValue) ? NumberValue.ToString("0") : NumberValue.ToString("0.##"))
        : "";
}

/// 「游戏设置」窗口的视图模型:读 options.txt → 目录化展示(模糊过滤)→ 改完写回。
public partial class GameSettingsViewModel : ObservableObject
{
    private readonly GameOptionsFile _file;
    /// 打开窗口时的「游戏语言跟随启动器」开关(显示用;写回时以行内容为准)
    private readonly bool _followLauncher;
    /// 语言行第一项「跟随启动器」的文案(窗口打开时固定,读/写用同一份)
    private readonly string _followLauncherText = Loc.T("S_GS_FollowLauncher");

    public ObservableCollection<SettingRow> Rows { get; } = new();

    public string FilePath { get; }
    public bool FileExists { get; }
    public bool FileMissing => !FileExists;
    public bool UsesLegacyKeys { get; }

    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _dirty;

    /// 搜索提示(Edge/QQ 式下拉候选;空格分词、全部命中才算)
    public ObservableCollection<SettingRow> Suggestions { get; } = new();
    [ObservableProperty] private bool _showSuggestions;
    private bool _suppressSuggestions;

    public string FormatHint => !FileExists
        ? Loc.T("S_GS_NoOptions")
        : Loc.T(UsesLegacyKeys ? "S_GS_FmtLegacy" : "S_GS_FmtNew");

    public GameSettingsViewModel(string gameRoot)
    {
        FilePath = Path.Combine(gameRoot, "options.txt");
        _file = new GameOptionsFile(FilePath);
        FileExists = _file.Exists;
        UsesLegacyKeys = _file.UsesLegacyKeyCodes();
        _followLauncher = SettingsStore.Load().GameLanguageFollows;
        BuildRows();
    }

    private void BuildRows()
    {
        Rows.Clear();

        foreach (var def in OptionCatalog.Options)
        {
            // 主键找不到时回退替代键(如 graphicsMode ↔ graphics)
            var key = def.Key;
            var value = _file.Get(key);
            if (value == null)
            {
                foreach (var fallback in def.FallbackKeys)
                {
                    value = _file.Get(fallback);
                    if (value != null) { key = fallback; break; }
                }
            }
            var missing = value == null;
            var row = new SettingRow
            {
                Key = key,
                Label = OptionCatalog.LabelFor(def),
                Kind = def.Kind,
                Min = def.Min,
                Max = def.Max,
                Step = def.Step,
                Choices = def.Kind == OptionKind.Choice && def.Key == "lang"
                    ? LanguageChoices()
                    : def.Choices,
                Hint = def.Hint,
                Missing = missing,
                // 搜索文本同时包含中英文标签(词典中文名 + 当前语言标签)与键/别名
                SearchText = string.Join(' ',
                    new[] { def.Label, OptionCatalog.LabelFor(def), key, def.Hint }.Concat(def.Aliases).Concat(def.FallbackKeys))
                    .ToLowerInvariant(),
            };
            if (value != null) ApplyValue(row, def, value);
            Rows.Add(row);
        }

        // 键位行(文件里有什么列什么;动作名兜底显示)
        foreach (var (action, value) in _file.Keybinds())
        {
            // options.txt 里是 key_key.forward 这种;展示用去掉内层 "key." 的动作名查中文
            var bareAction = action.StartsWith("key.", StringComparison.OrdinalIgnoreCase) ? action[4..] : action;
            var display = OptionCatalog.DisplayKey(value);
            Rows.Add(new SettingRow
            {
                Key = "key_" + action,
                // 英文界面直接显示动作名(forward/jump 本身就是英文;查表里没有的也用原名)
                Label = Loc.Current == "en-US"
                    ? bareAction
                    : OptionCatalog.KeybindNames.GetValueOrDefault(bareAction, bareAction),
                Kind = OptionKind.Keybind,
                KeyDisplay = display,
                TextValue = value,
                SearchText = $"键位 {bareAction} {display} {OptionCatalog.KeybindNames.GetValueOrDefault(bareAction, "")}".ToLowerInvariant(),
            });
        }

        ApplyFilter();
    }

    /// 语言行候选:第一项「跟随启动器」,然后是常用语言显示名
    private string[] LanguageChoices() =>
        new[] { _followLauncherText }.Concat(OptionCatalog.LanguageNames.Map.Keys).ToArray();

    private void ApplyValue(SettingRow row, OptionDef def, string value)
    {
        switch (def.Kind)
        {
            case OptionKind.Number:
                row.NumberValue = double.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : def.Min;
                break;
            case OptionKind.Toggle:
                row.BoolValue = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                break;
            case OptionKind.Choice when def.Key == "lang":
                // 跟随启动器开着时显示「跟随启动器」;关着时把文件里的语言代码转成显示名
                row.TextValue = _followLauncher
                    ? _followLauncherText
                    : OptionCatalog.LanguageNames.CodeToName(value);
                break;
            default:
                row.TextValue = value;
                break;
        }
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    /// 空格分词:每个词都要命中(词序无关、大小写不敏感)
    private void ApplyFilter()
    {
        var tokens = FilterText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                               .Select(t => t.ToLowerInvariant())
                               .ToArray();

        foreach (var row in Rows)
            row.Visible = tokens.Length == 0 || tokens.All(t => row.SearchText.Contains(t));

        Suggestions.Clear();
        if (tokens.Length > 0)
        {
            // 候选:label 以首个词开头的优先,然后按命中顺序;最多 8 条
            foreach (var row in Rows.Where(r => r.Visible)
                         .OrderByDescending(r => r.Label.ToLowerInvariant().StartsWith(tokens[0]))
                         .Take(8))
                Suggestions.Add(row);
        }

        ShowSuggestions = !_suppressSuggestions && Suggestions.Count > 0;
    }

    /// 点选候选:筛到该行并收起下拉
    public void PickSuggestion(SettingRow row)
    {
        _suppressSuggestions = true;
        FilterText = row.Label;
        _suppressSuggestions = false;
        ShowSuggestions = false;
    }

    public void MarkDirty() => Dirty = true;

    public void Reload()
    {
        var fresh = new GameOptionsFile(FilePath);
        // 重新构建(简单可靠:直接重建行)
        var vm = new GameSettingsViewModel(Path.GetDirectoryName(FilePath) ?? "");
        Rows.Clear();
        foreach (var row in vm.Rows) Rows.Add(row);
        StatusText = Loc.T("S_GS_Reloaded");
        Dirty = false;
        ApplyFilter();
    }

    public void Save()
    {
        if (!FileExists)
        {
            StatusText = "没有 options.txt 可保存——先启动一次游戏";
            return;
        }

        var changed = 0;
        var index = 0;
        foreach (var def in OptionCatalog.Options)
        {
            if (index >= Rows.Count) break;
            var row = Rows[index++];
            if (row.Missing) continue;

            string? newValue = def.Kind switch
            {
                OptionKind.Number => row.NumberValue.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                OptionKind.Toggle => row.BoolValue ? "true" : "false",
                OptionKind.Choice when def.Key == "lang" => ResolveLang(row.TextValue),
                _ => row.TextValue,
            };
            if (newValue == null) continue;

            if (_file.Get(row.Key) != newValue)
            {
                _file.Set(row.Key, newValue);
                changed++;
            }
        }

        // 键位行
        foreach (var row in Rows.Where(r => r.Kind == OptionKind.Keybind))
        {
            if (_file.Get(row.Key) != row.TextValue)
            {
                _file.Set(row.Key, row.TextValue);
                changed++;
            }
        }

        try
        {
            _file.Save();
            StatusText = changed == 0 ? Loc.T("S_GS_NoChange") : Loc.F("S_GS_SavedN", changed);
            Dirty = false;
        }
        catch (Exception ex)
        {
            StatusText = Loc.T("S_GS_SaveFailed") + ex.Message;
            Services.LogService.Error("写入 options.txt 失败", ex);
        }
    }

    /// 语言:显示名 → 代码;并保持文件原有的大小写风格(旧版本是 zh_cn)。
    /// 选「跟随启动器」则用启动器语言的 MC 代码,并把开关写回启动器设置
    private string ResolveLang(string displayName)
    {
        string code;
        if (displayName == _followLauncherText)
        {
            code = Loc.MinecraftLangCode;
            PersistFollowFlag(true);
        }
        else
        {
            if (!OptionCatalog.LanguageNames.Map.TryGetValue(displayName, out var mapped))
                mapped = displayName;
            code = mapped;
            PersistFollowFlag(false);
        }
        var old = _file.Get("lang") ?? "";
        return old.Length > 0 && old == old.ToLowerInvariant() ? code.ToLowerInvariant() : code;
    }

    /// 「游戏语言跟随启动器」开关写回启动器设置(%APPDATA%\.mc-launcher\settings.json)
    private static void PersistFollowFlag(bool follows)
    {
        var settings = SettingsStore.Load();
        settings.GameLanguageFollows = follows;
        SettingsStore.Save(settings);
    }
}
