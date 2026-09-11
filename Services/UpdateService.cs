using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using McLauncher.Models;

namespace McLauncher.Services;

/// 更新检查结果。Checked=false = 检查无法进行(服务器未配置 / 离线),此时反馈闸门不应拦截。
public sealed record UpdateCheckResult(bool Checked, bool HasUpdate, string LatestVersion, string Message);

/// 更新服务:四通道 + 全量包更新。
/// 服务端结构: {base}/updates/{channel}.json(清单: 版本/说明/包文件名/大小/sha256)+ 同目录 zip;
/// 更新流程: 检查 → 下载 → sha256 校验 → 解压到 .update/staging → 写替换脚本 → 退出重启(见 DownloadAndStageAsync)。
public sealed class UpdateService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static string ChannelName(UpdateChannel channel) => channel switch
    {
        UpdateChannel.Beta => Loc.T("S_ChBeta"),
        UpdateChannel.Alpha => Loc.T("S_ChAlpha"),
        UpdateChannel.Dev => Loc.T("S_ChDev"),
        _ => Loc.T("S_ChRelease"),
    };

    /// 服务器基址:设置里填的优先(自建/测试),否则用打包时内置的官方地址
    public static string? ResolveBaseUrl()
    {
        var url = SettingsStore.Load().UpdateBaseUrl;
        if (string.IsNullOrWhiteSpace(url)) url = AppInfo.UpdateBaseUrl;
        return string.IsNullOrWhiteSpace(url) ? null : url.TrimEnd('/');
    }

    /// "V0.2.0 SP0" → (0,2,0,0);无法识别返回 null
    public static (int Major, int Kernel, int Ui, int Sp)? ParseVersion(string? v)
    {
        var m = Regex.Match(v ?? "", @"^V?(\d+)\.(\d+)\.(\d+)\s*SP(\d+)$", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
                int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value));
    }

    private static int Compare((int Major, int Kernel, int Ui, int Sp) a, (int Major, int Kernel, int Ui, int Sp) b)
        => a.Major != b.Major ? a.Major - b.Major
         : a.Kernel != b.Kernel ? a.Kernel - b.Kernel
         : a.Ui != b.Ui ? a.Ui - b.Ui
         : a.Sp - b.Sp;

    private static string ManifestUrl(string baseUrl, UpdateChannel channel)
        => $"{baseUrl}/updates/{channel.ToString().ToLowerInvariant()}.json";

    public async Task<UpdateCheckResult> CheckAsync(UpdateChannel channel, string currentVersion)
    {
        var baseUrl = ResolveBaseUrl();
        if (baseUrl == null)
            return new UpdateCheckResult(false, false, "",
                Loc.F("S_Upd_NoServer", ChannelName(channel), currentVersion));
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(ManifestUrl(baseUrl, channel)));
            string latest = "";
            (int, int, int, int)? latestV = null;
            foreach (var r in doc.RootElement.GetProperty("releases").EnumerateArray())
            {
                var v = r.TryGetProperty("version", out var ve) ? ve.GetString() : null;
                var parsed = ParseVersion(v);
                if (parsed == null) continue;
                if (latestV == null || Compare(parsed.Value, latestV.Value) > 0) { latestV = parsed; latest = v!; }
            }

            var cur = ParseVersion(currentVersion);
            if (latestV == null || cur == null)
                return new UpdateCheckResult(false, false, "", Loc.T("S_Upd_BadManifest"));

            return Compare(latestV.Value, cur.Value) > 0
                ? new UpdateCheckResult(true, true, latest, Loc.F("S_Upd_Found", latest, currentVersion))
                : new UpdateCheckResult(true, false, latest, Loc.F("S_Upd_Latest", currentVersion));
        }
        catch (System.Net.Http.HttpRequestException hex) when (hex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // 通道还没发布内容(如 Beta/Alpha 空清单)→ 给用户看得懂的说明,而不是裸 404
            return new UpdateCheckResult(false, false, "", Loc.T("S_Upd_ChannelEmpty"));
        }
        catch (Exception ex)
        {
            LogService.Warn($"检查更新失败:{ex.Message}");
            return new UpdateCheckResult(false, false, "", Loc.T("S_Upd_Failed"));
        }
    }

    /// 下载全量包 → sha256 校验 → 解压到 .update/staging → 写好免交互替换脚本(调用方随后退出应用即可)
    public async Task<string> DownloadAndStageAsync(UpdateChannel channel, string version, Action<int>? progress = null)
    {
        var baseUrl = ResolveBaseUrl() ?? throw new InvalidOperationException("更新服务器未配置");
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(ManifestUrl(baseUrl, channel)));
        JsonElement? target = null;
        foreach (var r in doc.RootElement.GetProperty("releases").EnumerateArray())
            if (r.TryGetProperty("version", out var ve) && ve.GetString() == version) { target = r; break; }
        if (target == null) throw new InvalidOperationException($"清单里找不到版本 {version}");

        var full = target.Value.GetProperty("full");
        var file = full.GetProperty("file").GetString()!;
        var sha = full.GetProperty("sha256").GetString()!;

        var exeDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var updDir = Path.Combine(exeDir, ".update");
        var staging = Path.Combine(updDir, "staging");
        if (Directory.Exists(updDir)) Directory.Delete(updDir, true);
        Directory.CreateDirectory(staging);
        var zipPath = Path.Combine(updDir, "update.zip");

        using (var resp = await Http.GetAsync($"{baseUrl}/updates/{file}", HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? full.GetProperty("size").GetInt64();
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(zipPath);
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n));
                done += n;
                if (total > 0) progress?.Invoke((int)(done * 100 / total));
            }
        }

        var actual = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(zipPath))).ToLowerInvariant();
        if (!string.Equals(actual, sha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("更新包校验失败(sha256 不一致)");

        ZipFile.ExtractToDirectory(zipPath, staging, true);
        File.Delete(zipPath);

        // 替换脚本:等「本进程」退出 → 覆盖文件 → 重启 → 自清理(只等指定 PID,多开几份互不干扰)
        var ps1 = Path.Combine(exeDir, "apply-update.ps1");
        File.WriteAllText(ps1, """
            param([int]$TargetPid = 0)
            $root = Split-Path -Parent $MyInvocation.MyCommand.Path
            if ($TargetPid -gt 0) { while (Get-Process -Id $TargetPid -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 1 } }
            else { while (Get-Process NannyCraft -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 1 } }
            Copy-Item -Path (Join-Path $root ".update\staging\*") -Destination $root -Recurse -Force
            Remove-Item -Recurse -Force (Join-Path $root ".update")
            Start-Process (Join-Path $root "NannyCraft.exe")
            Remove-Item -Force $MyInvocation.MyCommand.Path
            """);
        return ps1;
    }
}
