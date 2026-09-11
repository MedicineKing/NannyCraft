using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace McLauncher.Services;

/// 文件下载服务:流式下载 + SHA-1 校验 + .part 原子替换 + 进度回调。
/// (差分更新(hdiffz/hpatchz)、镜像切换、并发队列都挂在这一层扩展)
public sealed class DownloadService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    public async Task DownloadAsync(string url, string destPath, string? sha1, IProgress<double>? progress, CancellationToken ct = default, string? sha256 = null)
    {
        var dir = Path.GetDirectoryName(destPath);
        if (dir is { Length: > 0 }) Directory.CreateDirectory(dir);
        var partPath = destPath + ".part";

        using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? -1;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = File.Create(partPath);

            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0) progress?.Report((double)done / total);
            }
        }

        if (sha256 is { Length: > 0 })
        {
            var actual = ComputeSha256(partPath);
            if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(partPath);
                throw new InvalidDataException($"SHA-256 校验失败: {Path.GetFileName(destPath)}");
            }
        }
        else if (sha1 is { Length: > 0 })
        {
            var actual = ComputeSha1(partPath);
            if (!string.Equals(actual, sha1, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(partPath);
                throw new InvalidDataException($"SHA-1 校验失败: {Path.GetFileName(destPath)}");
            }
        }

        File.Move(partPath, destPath, overwrite: true);
    }

    public static string ComputeSha1(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
