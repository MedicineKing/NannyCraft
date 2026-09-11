using System.IO;
using System.Text.Json;
using McLauncher.Models;

namespace McLauncher.Services;

/// 启动器设置(持久化到 %APPDATA%\.mc-launcher\settings.json)
public sealed class LauncherSettings
{
    public int MaxMemoryMb { get; set; } = 4096;
    public string GameRoot { get; set; } = "";
    public string JvmArgs { get; set; } = "";
    public List<string> ExtraJavaHomes { get; set; } = new();
    /// 当前版本的安装日期(yyyy-MM-dd):首次运行或更新到新版本时刷新
    public string InstallDate { get; set; } = "";
    /// 上次运行的版本号:用于判定"装上了新版本"→ 刷新安装日期
    public string LastVersion { get; set; } = "";
    /// 更新通道(UpdateChannel 名):Release / Beta / Alpha / Dev
    public string UpdateChannel { get; set; } = "Release";
    /// 主题色(十六进制,默认拂晓蓝)
    public string AccentColor { get; set; } = "#5B8CF5";
    /// 主页背景图路径(空 = 默认渐变)
    public string BackgroundImage { get; set; } = "";
    /// 离线模式用户名(启动参数 --username)
    public string Username { get; set; } = "Player";
    /// 下载并发数(16-1024;资源小文件多,大文件自动降到 1/4)
    public int DownloadConcurrency { get; set; } = 256;
    /// 全局动画速度倍率(0.5-2,1 = 标准)
    public double AnimationSpeed { get; set; } = 1.0;
    /// 主页未选版本时显示「今日 MC 快讯」(关 = 显示"选择版本查看详情")
    public bool ShowNewsOnIdle { get; set; } = true;
    /// 快讯源地址(空 = 官方 launchercontent.mojang.com;可填本机转发/镜像)
    public string NewsSourceUrl { get; set; } = "";
    /// 快讯代理(如 http://127.0.0.1:7890;空 = 直连;翻译请求走同一代理)
    public string NewsProxy { get; set; } = "";
    /// 快讯自动翻译(英文 → 简体中文)
    public bool NewsTranslate { get; set; } = true;
    /// 启动器唯一标识(首次运行生成,反馈时自动附带)
    public string LauncherId { get; set; } = "";
    /// 反馈仓库地址(空 = 用官方仓库 MedicineKing/NannyCraft)
    public string FeedbackRepo { get; set; } = "";
}

public static class SettingsStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".mc-launcher");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static string DefaultGameRoot => Path.Combine(Dir, "game");

    public static LauncherSettings Load()
    {
        LauncherSettings? settings = null;
        try
        {
            if (File.Exists(FilePath))
                settings = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(FilePath));
        }
        catch { /* 损坏则重置, 不阻塞启动 */ }
        settings ??= new LauncherSettings();

        // 首次运行或刚更新到新版本 → 记录安装日期
        if (string.IsNullOrWhiteSpace(settings.InstallDate) || settings.LastVersion != AppInfo.VersionText)
        {
            settings.InstallDate = DateTime.Now.ToString("yyyy-MM-dd");
            settings.LastVersion = AppInfo.VersionText;
            Save(settings);
        }
        return settings;
    }

    public static void Save(LauncherSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 保存失败下轮再试 */ }
    }
}
