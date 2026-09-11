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

        var body = new StringBuilder();
        if (kind == FeedbackKind.Bug)
        {
            body.AppendLine("### 问题描述");
            body.AppendLine("<!-- 发生了什么、期望是什么 -->");
            body.AppendLine();
            body.AppendLine("### 复现步骤");
            body.AppendLine("1. ");
            body.AppendLine();
        }
        else
        {
            body.AppendLine("### 想加的功能 / 改进");
            body.AppendLine("<!-- 说说你的想法 -->");
            body.AppendLine();
        }

        body.AppendLine("### 环境信息(自动填充,可修改)");
        body.AppendLine(BuildEnvironment(launcherId, javaText));

        if (kind == FeedbackKind.Bug)
        {
            body.AppendLine("### 运行日志(自动截取尾部)");
            body.AppendLine("```log");
            body.AppendLine(LogService.Tail());
            body.AppendLine("```");

            if (LogService.CrashTail() is { Length: > 0 } crash)
            {
                body.AppendLine("### 崩溃日志(自动附带)");
                body.AppendLine("```log");
                body.AppendLine(crash);
                body.AppendLine("```");
            }
        }

        var baseUrl = repoUrl.TrimEnd('/');
        return $"{baseUrl}/issues/new?title={Uri.EscapeDataString(title)}&labels={Uri.EscapeDataString(labels)}&body={Uri.EscapeDataString(body.ToString())}";
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
