using System.Net.Http;
using System.Text.Json;
using McLauncher.Models;

namespace McLauncher.Services;

/// Mojang 官方清单服务。
/// version_manifest_v2 → 版本列表;单版本 JSON → javaVersion 官方映射(零维护, 不猜测)。
public sealed class ManifestService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    private readonly Dictionary<string, VersionDetail> _detailCache = new();

    /// 拉取全部版本(官方按发布时间倒序返回)
    public async Task<IReadOnlyList<VersionEntry>> GetVersionsAsync(CancellationToken ct = default)
    {
        await using var stream = await Http.GetStreamAsync(ManifestUrl, ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var list = new List<VersionEntry>();
        foreach (var v in doc.RootElement.GetProperty("versions").EnumerateArray())
        {
            list.Add(new VersionEntry
            {
                Id = v.GetProperty("id").GetString() ?? "",
                Type = v.GetProperty("type").GetString() ?? "",
                Url = v.GetProperty("url").GetString() ?? "",
                ReleaseTime = v.TryGetProperty("releaseTime", out var rt) ? rt.GetDateTimeOffset() : default,
            });
        }
        return list;
    }

    /// 单版本详情(带缓存);javaVersion 字段即官方 JDK 映射
    public async Task<VersionDetail> GetDetailAsync(VersionEntry entry, CancellationToken ct = default)
    {
        if (_detailCache.TryGetValue(entry.Id, out var cached))
            return cached;

        var json = await Http.GetStringAsync(entry.Url, ct);
        var detail = JsonSerializer.Deserialize<VersionDetail>(json)
            ?? throw new InvalidOperationException($"版本 JSON 解析失败: {entry.Id}");
        _detailCache[entry.Id] = detail;
        return detail;
    }
}
