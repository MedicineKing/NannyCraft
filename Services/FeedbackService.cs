using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace McLauncher.Services;

public enum FeedbackKind { Bug, Idea }

/// 反馈服务:收集环境信息(系统/硬件/日志),拼 GitHub Issues 新建页链接。
public static class FeedbackService
{
    /// 生成 Issues 新建链接(标题、标签、正文全部预填;正文含可编辑的"问题描述"占位)
    public static string BuildIssueUrl(string repoUrl, FeedbackKind kind, string launcherId, string javaText)
    {
        var title = kind == FeedbackKind.Bug ? "[Bug] " : "[建议] ";
        var labels = kind == FeedbackKind.Bug ? "bug" : "enhancement";

        var head = new StringBuilder();
        if (kind == FeedbackKind.Bug)
        {
            head.AppendLine("### 问题描述");
            head.AppendLine("<!-- 发生了什么、期望是什么 -->");
            head.AppendLine();
            head.AppendLine("### 复现步骤");
            head.AppendLine("1. ");
            head.AppendLine();
        }
        else
        {
            head.AppendLine("### 想加的功能 / 改进");
            head.AppendLine("<!-- 说说你的想法 -->");
            head.AppendLine();
        }

        head.AppendLine("### 环境信息(自动填充,可修改)");
        head.AppendLine(BuildEnvironment(launcherId, javaText));

        // GitHub 对超长 URL 直接 414(HTTP/2 下浏览器表现为协议错误)
        // → 正文按"编码后长度"预算裁剪:先舍崩溃段,再逐步缩短日志
        const int MaxEncodedBody = 5000;
        var logText = kind == FeedbackKind.Bug ? LogService.Tail() : null;
        var crashText = kind == FeedbackKind.Bug ? LogService.CrashTail() : null;

        var body = Compose(head.ToString(), logText, crashText);
        if (crashText != null && Uri.EscapeDataString(body).Length > MaxEncodedBody)
            body = Compose(head.ToString(), logText, null);
        while (Uri.EscapeDataString(body).Length > MaxEncodedBody && logText is { Length: > 300 })
        {
            logText = logText[^(logText.Length * 3 / 4)..]; // 每次保留最新 3/4
            body = Compose(head.ToString(), logText, null);
        }

        var baseUrl = repoUrl.TrimEnd('/');
        return $"{baseUrl}/issues/new?title={Uri.EscapeDataString(title)}&labels={Uri.EscapeDataString(labels)}&body={Uri.EscapeDataString(body)}";
    }

    private static string Compose(string head, string? logText, string? crashText)
    {
        var sb = new StringBuilder(head);
        if (!string.IsNullOrEmpty(logText))
        {
            sb.AppendLine("### 运行日志(自动截取尾部)");
            sb.AppendLine("```log");
            sb.AppendLine(logText);
            sb.AppendLine("```");
        }
        if (!string.IsNullOrEmpty(crashText))
        {
            sb.AppendLine("### 崩溃日志(自动附带)");
            sb.AppendLine("```log");
            sb.AppendLine(crashText);
            sb.AppendLine("```");
        }
        return sb.ToString();
    }

    /// 环境信息(markdown 列表):启动器标识 / 系统 / CPU / 内存 / GPU / 运行环境 / Java
    public static string BuildEnvironment(string launcherId, string javaText)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"- 启动器版本: {AppInfoVersion()}");
        sb.AppendLine($"- 启动器标识: {launcherId}");
        sb.AppendLine($"- 系统: {OsName()} ({RuntimeInformation.OSArchitecture})");
        sb.AppendLine($"- CPU: {CpuName()}({Environment.ProcessorCount} 线程)");
        sb.AppendLine($"- 内存: {TotalMemory()}");
        if (GpuName() is { Length: > 0 } gpu)
            sb.AppendLine($"- GPU: {gpu}");
        sb.AppendLine($"- 运行环境: {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"- Java: {javaText}");
        return sb.ToString().TrimEnd();
    }

    private static string AppInfoVersion() => Models.AppInfo.VersionText;

    /// 本机指纹(Windows MachineGuid 的 SHA-256 前 8 位)。
    /// 仅本地使用:校验"配置被整体拷到另一台机器"时重生成安装段,保证标识唯一。
    public static string MachineFingerprint()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            var guid = key?.GetValue("MachineGuid") as string;
            if (!string.IsNullOrWhiteSpace(guid))
                return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(guid)))[..8].ToLowerInvariant();
        }
        catch { }
        return "00000000";
    }

    private static string OsName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var product = key?.GetValue("ProductName") as string ?? "Windows";
            var build = key?.GetValue("CurrentBuildNumber") as string ?? Environment.OSVersion.Version.Build.ToString();
            return $"{product} {Environment.OSVersion.Version.Major}.{Environment.OSVersion.Version.Minor}.{build}";
        }
        catch { return Environment.OSVersion.VersionString; }
    }

    private static string CpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "未知 CPU";
        }
        catch { return "未知 CPU"; }
    }

    private static string GpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000");
            return (key?.GetValue("DriverDesc") as string)?.Trim() ?? "";
        }
        catch { return ""; }
    }

    private static string TotalMemory()
    {
        try
        {
            var status = new MemoryStatusEx();
            if (GlobalMemoryStatusEx(status))
                return $"{status.ullTotalPhys / 1024.0 / 1024 / 1024:F1} GB";
        }
        catch { }
        return "未知";
    }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class MemoryStatusEx
    {
        public uint dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx lpBuffer);
}
