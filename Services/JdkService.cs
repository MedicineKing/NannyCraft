using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace McLauncher.Services;

/// 内置 Java 运行时:按官方 javaVersion 映射自动下载对应 JDK(Adoptium/Temurin)。
/// 包带 SHA-256 校验;解压到 %APPDATA%\.mc-launcher\java\jdk-{特征版本};清华镜像可选(国内加速)。
public sealed class JdkService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public static string JavaRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".mc-launcher", "java");

    /// 已就位则直接返回 java.exe;否则下载 + 校验 + 解压。返回 null 表示失败。
    public async Task<string?> EnsureAsync(int featureVersion, bool useTunaMirror,
        IProgress<double>? progress, CancellationToken ct = default)
    {
        var target = Path.Combine(JavaRoot, $"jdk-{featureVersion}");
        var javaExe = Path.Combine(target, "bin", "java.exe");
        if (File.Exists(javaExe)) return javaExe;

        // 1) 查询 Adoptium 最新 build(附 SHA-256)
        var api = $"https://api.adoptium.net/v3/assets/latest/{featureVersion}/hotspot" +
                  "?architecture=x64&image_type=jdk&os=windows&vendor=eclipse";
        var json = await Http.GetStringAsync(api, ct);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.GetArrayLength() == 0)
            throw new InvalidOperationException($"没有找到 Java {featureVersion} 的 Windows x64 构建");

        var package = doc.RootElement[0].GetProperty("binary").GetProperty("package");
        var officialUrl = package.GetProperty("link").GetString()
                          ?? throw new InvalidOperationException("下载地址缺失");
        var sha256 = package.GetProperty("checksum").GetString();
        var fileName = package.GetProperty("name").GetString() ?? $"jdk-{featureVersion}.zip";

        // 2) 下载(可选清华镜像,失败自动回退官方)
        var zipPath = Path.Combine(Path.GetTempPath(), "nannycraft-" + fileName);
        var download = new DownloadService();
        var mirrorUrl = $"https://mirrors.tuna.tsinghua.edu.cn/Adoptium/{featureVersion}/jdk/x64/windows/{fileName}";

        var candidates = useTunaMirror ? new[] { mirrorUrl, officialUrl } : new[] { officialUrl };
        Exception? lastError = null;
        foreach (var url in candidates)
        {
            try
            {
                LogService.Info($"下载 Java {featureVersion}:{url}");
                await download.DownloadAsync(url, zipPath, null, progress, ct, sha256);
                lastError = null;
                break;
            }
            catch (Exception ex)
            {
                lastError = ex;
                LogService.Warn($"Java 下载失败(将尝试下一源):{ex.Message}");
            }
        }
        if (lastError != null) throw lastError;

        // 3) 解压到临时目录,再原子移入(压缩包内是单层 jdk-xx 目录)
        var staging = Path.Combine(Path.GetTempPath(), "nannycraft-jdk-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            ZipFile.ExtractToDirectory(zipPath, staging, overwriteFiles: true);
            var inner = Directory.GetDirectories(staging).FirstOrDefault() ?? staging;

            Directory.CreateDirectory(JavaRoot);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(inner, target);
            LogService.Info($"Java {featureVersion} 就位:{javaExe}");
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch { }
            try { File.Delete(zipPath); } catch { }
        }

        return File.Exists(javaExe) ? javaExe : null;
    }
}
