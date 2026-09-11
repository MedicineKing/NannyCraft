using McLauncher.Models;

namespace McLauncher.Services;

/// 更新服务:四通道(正式版 / Beta 内测 / Alpha 内测 / Dev 内测)+ 增量差分更新(规划)。
/// 更新服务器还没搭;搭好后在这里接清单接口(按通道取版本清单、比对当前版本、拉差分/整包,校验后原子替换)。
public sealed class UpdateService
{
    public static string ChannelName(UpdateChannel channel) => channel switch
    {
        UpdateChannel.Beta => "Beta 内测",
        UpdateChannel.Alpha => "Alpha 内测",
        UpdateChannel.Dev => "Dev 内测",
        _ => "正式版",
    };

    public Task<string> CheckAsync(UpdateChannel channel, string currentVersion)
        => Task.FromResult($"[{ChannelName(channel)}] 更新服务器尚未搭建 —— 四通道与版本号架构已就位,服务器就绪后此处显示检查结果({currentVersion})");
}
