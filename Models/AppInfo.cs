using System.IO;
using System.Reflection;

namespace McLauncher.Models;

/// 更新通道:正式版 / Beta 内测 / Alpha 内测 / Dev 内测
public enum UpdateChannel { Release, Beta, Alpha, Dev }

public sealed record UpdateChannelOption(UpdateChannel Channel, string NameKey)
{
    public string Name => Services.Loc.T(NameKey);
}

/// 动画速度档位(借鉴 PCL 的全局动画速度设置)
public sealed record AnimationSpeedOption(string NameKey, double Value)
{
    public string Name => Services.Loc.T(NameKey);
}

/// 启动器语言选项
public sealed record LanguageOption(string Code, string Name);

/// 主题色选项(设置页色板;IsSelected 驱动选中环)
public partial class AccentOption : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public string Name { get; init; } = "";
    public string Hex { get; init; } = "";
    public System.Windows.Media.Color Color { get; init; }
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isSelected;

    private System.Windows.Media.SolidColorBrush? _brush;

    /// 色板圆点用画刷(Fill 需要 Brush,不能直接绑 Color)
    public System.Windows.Media.SolidColorBrush Brush
    {
        get
        {
            if (_brush == null)
            {
                _brush = new System.Windows.Media.SolidColorBrush(Color);
                _brush.Freeze();
            }
            return _brush;
        }
    }
}

/// 应用自身信息。版本号架构 = V{主版本}.{内核版本}.{UI 版本} SP{修复的 bug 数}(发版时手动 +1)。
public static class AppInfo
{
    /// 显示名(标题栏 / 关于页 / 日志 / 启动参数;内部命名空间仍为 McLauncher)
    public const string DisplayName = "NannyCraft";

    /// 官方仓库(「帮助与反馈」跳转目标;设置里的「反馈仓库地址」可覆盖)
    public const string RepoUrl = "https://github.com/MedicineKing/NannyCraft";

    /// QQ 群反馈链接(qm.qq.com 加群链接;留空 = 按钮提示"尚未配置")
    public const string QQGroupUrl = "https://qm.qq.com/q/QHZpJaveSG";

    /// 更新服务器基址(如 http://your-host:8789;留空 = 未配置,检查更新会提示)。
    /// 部署后填入此处打包发布;设置里的「更新服务器地址」可临时覆盖(测试/自建)。
    public const string UpdateBaseUrl = "";

    /// 构建标识:编译期由 版本号+构建日期 派生并"嵌进二进制"(同一构建恒定,不同构建不同)
    public static string BuildTag { get; } =
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(VersionText + "|" + BuildDate)))[..6].ToLowerInvariant();

    public const int MajorVersion = 0;
    public const int KernelVersion = 2;
    public const int UiVersion = 1;
    public const int PatchLevel = 0;

    public static string VersionText => $"V{MajorVersion}.{KernelVersion}.{UiVersion} SP{PatchLevel}";

    /// 构建日期:编译时注入程序集元数据;读不到时回退到 exe 文件时间
    public static string BuildDate
    {
        get
        {
            var meta = typeof(AppInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "BuildDate")?.Value;
            if (!string.IsNullOrWhiteSpace(meta)) return meta;
            try { return File.GetLastWriteTime(Environment.ProcessPath!).ToString("yyyy-MM-dd"); }
            catch { return "未知"; }
        }
    }
}
