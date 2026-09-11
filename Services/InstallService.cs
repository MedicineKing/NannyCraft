using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using McLauncher.Models;

namespace McLauncher.Services;

/// 库解析(检查与安装共用):规则过滤 → 需要的库文件 / natives 分类器清单。
public static class LibraryResolver
{
    /// 官方 rules 判定:无规则=通过;有规则=按最后一条命中的 allow/disallow 决定
    public static bool PassesRules(List<RuleEntry>? rules)
    {
        if (rules is not { Count: > 0 }) return true;
        var allowed = false;
        foreach (var rule in rules)
        {
            if (!RuleMatches(rule)) continue;
            allowed = rule.Action == "allow";
        }
        return allowed;
    }

    private static bool RuleMatches(RuleEntry rule)
    {
        if (rule.Os is { } os)
        {
            if (os.Name is { Length: > 0 } name &&
                !string.Equals(name, "windows", StringComparison.OrdinalIgnoreCase))
                return false;
            if (os.Arch is { Length: > 0 } arch && !string.Equals(arch, CurrentArch(), StringComparison.OrdinalIgnoreCase))
                return false;
            // os.version 不作判定(现代库基本不用;避免误杀)
        }

        // features:当前不支持演示模式 / 自定义分辨率 → 任何要求 true 的特性都不命中
        if (rule.Features is { Count: > 0 })
        {
            foreach (var feature in rule.Features)
            {
                if (feature.Value && !FeatureEnabled(feature.Key)) return false;
            }
        }
        return true;
    }

    private static bool FeatureEnabled(string key) => false;

    private static string CurrentArch() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x86_64",
        Architecture.X86 => "x86",
        Architecture.Arm64 => "arm64",
        _ => "x86_64",
    };

    /// 通过规则过滤、且带 artifact 的普通库(maven 相对路径 + 文件信息)
    public static List<(string RelPath, DownloadFile File)> RequiredArtifacts(VersionDetail detail)
    {
        var result = new List<(string, DownloadFile)>();
        foreach (var lib in detail.Libraries ?? new List<LibraryEntry>())
        {
            if (!PassesRules(lib.Rules)) continue;
            var artifact = lib.Downloads?.Artifact;
            if (artifact?.Path is not { Length: > 0 } rel || artifact.Url is not { Length: > 0 }) continue;
            result.Add((rel, artifact));
        }
        return result;
    }

    /// 旧格式 natives 分类器(下载后解压到 natives 目录)
    public static List<(string RelPath, DownloadFile File, List<string>? Exclude)> RequiredNatives(VersionDetail detail)
    {
        var result = new List<(string, DownloadFile, List<string>?)>();
        foreach (var lib in detail.Libraries ?? new List<LibraryEntry>())
        {
            if (!PassesRules(lib.Rules)) continue;
            if (lib.Natives is not { Count: > 0 } natives) continue;

            // 按当前系统找分类器键(windows / windows-arm64 …)
            var key = natives.TryGetValue(NativeOsKey(), out var k) ? k
                    : natives.TryGetValue("windows", out var w) ? w
                    : null;
            if (key == null) continue;

            if (lib.Downloads?.Classifiers?.TryGetValue(key, out var file) == true &&
                file.Path is { Length: > 0 } rel && file.Url is { Length: > 0 })
            {
                result.Add((rel, file, lib.Extract?.Exclude));
            }
        }
        return result;
    }

    private static string NativeOsKey() =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "windows-arm64" : "windows";
}

public sealed record InstallProgress(string Phase, int Done, int Total, string Current);

/// 完整安装链:客户端 jar → 库(规则过滤)→ natives(下载+解压)→ 资源对象树 → 日志配置。
/// 全部带 SHA-1 校验、已存在且大小一致则跳过;并发下载,进度按文件数上报。
public sealed class InstallService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private const string AssetBase = "https://resources.download.minecraft.net/";

    private readonly DownloadService _download = new();

    public async Task InstallAsync(VersionDetail detail, string gameRoot, int concurrency, IProgress<InstallProgress>? progress, CancellationToken ct = default)
    {
        // 资源对象多为 KB 级小文件 → 全速;库/客户端是大文件 → 降到 1/4,避免把带宽花在握手
        var assetWorkers = Math.Clamp(concurrency, 8, 1024);
        var fileWorkers = Math.Clamp(concurrency / 4, 4, 128);
        var versionDir = Path.Combine(gameRoot, "versions", detail.Id);
        var nativesDir = Path.Combine(versionDir, "natives");
        var assetsDir = Path.Combine(gameRoot, "assets");
        Directory.CreateDirectory(versionDir);

        // ── 收集文件任务 ──
        var jobs = new List<(string Url, string Dest, string? Sha1, long Size, string Name)>();

        var client = detail.Downloads?.Client;
        if (client?.Url is { Length: > 0 } clientUrl)
            jobs.Add((clientUrl, Path.Combine(versionDir, $"{detail.Id}.jar"), client.Sha1, client.Size, $"{detail.Id}.jar"));

        foreach (var (rel, file) in LibraryResolver.RequiredArtifacts(detail))
            jobs.Add((file.Url!, Path.Combine(gameRoot, "libraries", rel.Replace('/', Path.DirectorySeparatorChar)), file.Sha1, file.Size, rel));

        var natives = LibraryResolver.RequiredNatives(detail);
        foreach (var (rel, file, _) in natives)
            jobs.Add((file.Url!, Path.Combine(gameRoot, "libraries", rel.Replace('/', Path.DirectorySeparatorChar)), file.Sha1, file.Size, rel));

        var index = detail.AssetIndex;
        string? assetIndexPath = null;
        if (index?.Url is { Length: > 0 } indexUrl)
        {
            assetIndexPath = Path.Combine(assetsDir, "indexes", index.Id + ".json");
            jobs.Add((indexUrl, assetIndexPath, index.Sha1, 0, $"assets/indexes/{index.Id}.json"));
        }

        var logConfig = detail.Logging?.Client?.File;
        if (logConfig?.Url is { Length: > 0 } logUrl)
            jobs.Add((logUrl, Path.Combine(assetsDir, "log_configs", Path.GetFileName(logConfig.Path ?? "client.xml")), logConfig.Sha1, logConfig.Size, "log_configs"));

        // ── 阶段一:并发下载(已存在且大小一致则跳过)──
        var done = 0;
        var total = jobs.Count;
        progress?.Report(new("准备", 0, total, ""));
        await Parallel.ForEachAsync(jobs,
            new ParallelOptions { MaxDegreeOfParallelism = fileWorkers, CancellationToken = ct },
            async (job, token) =>
            {
                var exists = File.Exists(job.Dest) &&
                             (job.Size <= 0 || new FileInfo(job.Dest).Length == job.Size);
                if (!exists)
                    await _download.DownloadAsync(job.Url, job.Dest, job.Sha1, null, token);

                var current = Interlocked.Increment(ref done);
                progress?.Report(new("文件", current, total, job.Name));
            });

        // ── 阶段二:natives 解压(META-INF 与 exclude 规则剔除,平铺文件名)──
        progress?.Report(new("解压 natives", 0, natives.Count, ""));
        for (var i = 0; i < natives.Count; i++)
        {
            var (rel, _, exclude) = natives[i];
            var jarPath = Path.Combine(gameRoot, "libraries", rel.Replace('/', Path.DirectorySeparatorChar));
            ExtractNatives(jarPath, nativesDir, exclude);
            progress?.Report(new("解压 natives", i + 1, natives.Count, Path.GetFileName(rel)));
        }

        // ── 阶段三:资源对象树(数量大,哈希寻址,已存在跳过)──
        if (assetIndexPath != null && File.Exists(assetIndexPath))
            await DownloadAssetsAsync(assetIndexPath, assetsDir, assetWorkers, progress, ct);

        progress?.Report(new("完成", 1, 1, ""));
    }

    private async Task DownloadAssetsAsync(string indexPath, string assetsDir, int workers, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(indexPath, ct));
        if (!doc.RootElement.TryGetProperty("objects", out var objects)) return;

        var jobs = new List<(string Hash, long Size)>();
        foreach (var prop in objects.EnumerateObject())
        {
            if (!prop.Value.TryGetProperty("hash", out var hashEl) || !prop.Value.TryGetProperty("size", out var sizeEl)) continue;
            var hash = hashEl.GetString();
            if (hash is not { Length: > 2 }) continue;
            jobs.Add((hash, sizeEl.GetInt64()));
        }

        var done = 0;
        var total = jobs.Count;
        progress?.Report(new("资源", 0, total, ""));
        await Parallel.ForEachAsync(jobs,
            new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = ct },
            async (job, token) =>
            {
                var prefix = job.Hash[..2];
                var dest = Path.Combine(assetsDir, "objects", prefix, job.Hash);
                if (!(File.Exists(dest) && new FileInfo(dest).Length == job.Size))
                    await _download.DownloadAsync(AssetBase + prefix + "/" + job.Hash, dest, job.Hash, null, token);

                var current = Interlocked.Increment(ref done);
                if (current % 10 == 0 || current == total)
                    progress?.Report(new("资源", current, total, job.Hash));
            });
    }

    private static void ExtractNatives(string jarPath, string nativesDir, List<string>? exclude)
    {
        if (!File.Exists(jarPath)) return;
        Directory.CreateDirectory(nativesDir);

        using var zip = ZipFile.OpenRead(jarPath);
        foreach (var entry in zip.Entries)
        {
            if (entry.Name.Length == 0) continue; // 目录项
            if (entry.FullName.StartsWith("META-INF", StringComparison.OrdinalIgnoreCase)) continue;
            if (exclude is { Count: > 0 } &&
                exclude.Any(prefix => entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                continue;

            // 官方行为:只取文件名平铺(避免 zip 内路径造成目录穿越)
            entry.ExtractToFile(Path.Combine(nativesDir, entry.Name), overwrite: true);
        }
    }
}
