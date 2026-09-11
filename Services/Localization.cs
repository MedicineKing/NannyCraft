using System.Windows;

namespace McLauncher.Services;

/// 启动器界面本地化:语言包 = Styles/Strings.{code}.xaml 资源字典。
/// 切换语言时整体替换合并字典——XAML 侧用 {DynamicResource S_xxx} 的文案会实时刷新。
public static class Loc
{
    public static readonly IReadOnlyList<(string Code, string Name)> Languages = new[]
    {
        ("zh-CN", "简体中文"),
        ("en-US", "English"),
    };

    public static string Current { get; private set; } = "zh-CN";

    /// 当前语言的 MC 语言代码(用于"游戏语言跟随启动器")
    public static string MinecraftLangCode => Current switch
    {
        "en-US" => "en_us",
        _ => "zh_CN",
    };

    public static void Apply(string? code)
    {
        Current = Languages.Any(l => l.Code == code) ? code! : "zh-CN";

        var app = Application.Current?.Resources;
        if (app == null) return;

        for (var i = app.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            var source = app.MergedDictionaries[i].Source?.OriginalString ?? "";
            if (source.Contains("Strings.")) app.MergedDictionaries.RemoveAt(i);
        }
        app.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"Styles/Strings.{Current}.xaml", UriKind.Relative),
        });
    }

    /// 代码里取词条(设置类动态文案用)
    public static string T(string key) => Application.Current?.TryFindResource(key) as string ?? key;

    public static string F(string key, params object[] args) => string.Format(T(key), args);
}
