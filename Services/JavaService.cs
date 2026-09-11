using System.IO;
using Microsoft.Win32;
using McLauncher.Models;

namespace McLauncher.Services;

/// 本机 Java 探测(保姆式:能找的都给你找出来)。
/// 顺序:JAVA_HOME → 注册表(各家 JDK 发行版)→ 常见安装目录 → PATH。
/// 版本号优先读 JDK 自带的 release 文件(零进程、秒出),失败时归零(未知)。
public sealed class JavaService
{
    public IReadOnlyList<JavaRuntime> FindInstalled()
    {
        var found = new List<JavaRuntime>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAdd(string? home, JavaSource source)
        {
            if (string.IsNullOrWhiteSpace(home)) return;
            string javaExe;
            try { javaExe = Path.GetFullPath(Path.Combine(home, "bin", "java.exe")); }
            catch { return; }
            if (!File.Exists(javaExe)) return;
            var norm = Path.GetFullPath(home);
            if (!seen.Add(norm)) return;
            found.Add(new JavaRuntime { Home = norm, JavaExe = javaExe, Version = ReadVersion(norm), Source = source });
        }

        // 0) 内置运行时(由 JdkService 下载到数据目录,优先级最高)
        try
        {
            if (Directory.Exists(JdkService.JavaRoot))
                foreach (var dir in Directory.GetDirectories(JdkService.JavaRoot))
                    TryAdd(dir, JavaSource.Bundled);
        }
        catch { /* 忽略损坏的内置目录 */ }

        // 1) JAVA_HOME
        TryAdd(Environment.GetEnvironmentVariable("JAVA_HOME"), JavaSource.JavaHome);

        // 2) 注册表(常见发行版)
        string[] registryRoots =
        {
            @"SOFTWARE\JavaSoft\JDK",
            @"SOFTWARE\JavaSoft\Java Development Kit",
            @"SOFTWARE\Eclipse Adoptium\JDK",
            @"SOFTWARE\Azul Systems\Zulu",
            @"SOFTWARE\Microsoft\JDK",
        };
        foreach (var root in registryRoots)
            foreach (var home in ReadRegistryHomes(root))
                TryAdd(home, JavaSource.Registry);

        // 3) 常见安装目录
        string[] commonDirs =
        {
            @"C:\Program Files\Java",
            @"C:\Program Files\Eclipse Adoptium",
            @"C:\Program Files\Amazon Corretto",
            @"C:\Program Files\Zulu",
            @"C:\Program Files (x86)\Java",
        };
        foreach (var baseDir in commonDirs)
        {
            if (!Directory.Exists(baseDir)) continue;
            foreach (var dir in Directory.GetDirectories(baseDir))
                TryAdd(dir, JavaSource.CommonDir);
        }

        // 4) PATH 中的 java.exe
        foreach (var segment in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            var seg = segment.Trim();
            if (seg.Length == 0) continue;
            if (File.Exists(Path.Combine(seg, "java.exe")))
                TryAdd(Path.GetFullPath(Path.Combine(seg, "..")), JavaSource.Path);
        }

        return found.OrderByDescending(j => j.Version).ToList();
    }

    private static IEnumerable<string> ReadRegistryHomes(string subKey)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKey);
            if (key == null) yield break;
            foreach (var versionName in key.GetSubKeyNames())
            {
                using var versionKey = key.OpenSubKey(versionName);
                if (versionKey?.GetValue("JavaHome") is string home && home.Length > 0)
                    yield return home;
            }
        }
        finally { /* using 已释放 */ }
    }

    /// 读 <home>/release 里的 JAVA_VERSION(不启动进程)
    private static int ReadVersion(string home)
    {
        try
        {
            var release = Path.Combine(home, "release");
            if (!File.Exists(release)) return 0;
            foreach (var line in File.ReadAllLines(release))
            {
                if (!line.StartsWith("JAVA_VERSION=", StringComparison.Ordinal)) continue;
                var raw = line.Split('=', 2)[1].Trim('"');
                var parts = raw.Split('.');
                // "21.0.4" -> 21;"1.8.0_402" -> 8
                if (parts.Length >= 2 && int.TryParse(parts[0], out var major))
                    return major == 1 && int.TryParse(parts[1], out var legacy) ? legacy : major;
                if (parts.Length == 1 && int.TryParse(parts[0], out var single))
                    return single;
            }
        }
        catch { /* 版本未知不致命 */ }
        return 0;
    }
}
