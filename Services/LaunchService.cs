using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using McLauncher.Models;

namespace McLauncher.Services;

/// 启动链路服务:完整性检查(与安装链同一套解析) → 按官方参数模板生成命令行 → 拉起进程。
public sealed class LaunchService
{
    /// 生成启动计划(纯函数,无副作用)。
    /// gameRoot = 共享根(库/资源/版本文件);gameDir = 实际 --gameDir(隔离版本指向 versions/&lt;id&gt;)。
    public LaunchPlan BuildPlan(VersionDetail detail, JavaRuntime? java, string gameRoot, string? gameDir, LaunchOptions options)
    {
        gameDir ??= gameRoot;

        var plan = new LaunchPlan();
        var versionDir = Path.Combine(gameRoot, "versions", detail.Id);
        var clientJar = Path.Combine(versionDir, $"{detail.Id}.jar");
        var nativesDir = Path.Combine(versionDir, "natives");

        if (java == null)
            plan.Missing.Add("未找到任何 Java(设置 JAVA_HOME 或安装 JDK 后重开)");
        else if (detail.JavaVersion is { MajorVersion: > 0 } hint && java.Version > 0 && java.Version < hint.MajorVersion)
            plan.Missing.Add($"这个版本需要 Java {hint.MajorVersion},当前最高的只有 Java {java.Version}");

        if (!File.Exists(clientJar))
            plan.Missing.Add($"缺少客户端文件 versions/{detail.Id}/{detail.Id}.jar");

        // classpath:客户端 jar + 规则过滤后的库(与安装器同源)
        var classpath = new List<string> { clientJar };
        var libraryMissing = new List<string>();
        foreach (var (rel, _) in LibraryResolver.RequiredArtifacts(detail))
        {
            var full = Path.Combine(gameRoot, "libraries", rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full)) classpath.Add(full);
            else libraryMissing.Add(rel);
        }
        plan.LibraryMissing = libraryMissing;
        if (libraryMissing.Count > 0)
            plan.Missing.Add($"缺少 {libraryMissing.Count} 个库文件");

        // natives:只要需要且目录为空视为缺失
        var natives = LibraryResolver.RequiredNatives(detail);
        if (natives.Count > 0 && (!Directory.Exists(nativesDir) || Directory.GetFiles(nativesDir).Length == 0))
            plan.Missing.Add("原生库(natives)还没解压");

        // 资源:索引文件在即视为就绪(安装器负责对象树完整性)
        var assetIndexName = detail.AssetIndex?.Id ?? detail.Assets;
        if (assetIndexName is { Length: > 0 })
        {
            var indexPath = Path.Combine(gameRoot, "assets", "indexes", assetIndexName + ".json");
            if (!File.Exists(indexPath)) plan.Missing.Add("游戏资源(assets)还没下载");
        }

        if (plan.Missing.Count > 0)
        {
            plan.CanLaunch = false;
            plan.Summary = $"还差 {plan.Missing.Count} 项没准备好";
            return plan;
        }

        var args = BuildArguments(detail, java!, classpath, nativesDir, gameRoot, gameDir!, options);
        plan.FileName = java!.JavaExe;
        plan.Arguments = string.Join(' ', args.Select(Quote));
        plan.CanLaunch = true;
        plan.Summary = "准备就绪";
        return plan;
    }

    /// 参数生成:1.13+ 走官方 arguments 模板(${...} 变量替换);旧版本走兜底拼装。
    private static List<string> BuildArguments(VersionDetail detail, JavaRuntime java, List<string> classpath,
        string nativesDir, string gameRoot, string gameDir, LaunchOptions options)
    {
        var assetsDir = Path.Combine(gameRoot, "assets");
        var playerName = string.IsNullOrWhiteSpace(options.PlayerName) ? "Player" : options.PlayerName.Trim();

        var vars = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_player_name"] = playerName,
            ["version_name"] = detail.Id,
            ["game_directory"] = gameDir,
            ["assets_root"] = assetsDir,
            ["assets_index_name"] = detail.AssetIndex?.Id ?? detail.Assets ?? "legacy",
            ["auth_uuid"] = OfflineUuid(playerName),
            ["auth_access_token"] = "0",
            ["auth_session"] = "0",
            ["clientid"] = "",
            ["auth_xuid"] = "",
            ["user_type"] = "legacy",
            ["version_type"] = "release",
            ["user_properties"] = "{}",
            ["natives_directory"] = nativesDir,
            ["launcher_name"] = AppInfo.DisplayName,
            ["launcher_version"] = AppInfo.VersionText,
            ["classpath"] = string.Join(Path.PathSeparator, classpath),
            ["classpath_separator"] = Path.PathSeparator.ToString(),
            ["library_directory"] = Path.Combine(gameRoot, "libraries"),
        };

        var args = new List<string>
        {
            $"-Xmx{options.MaxMemoryMb}M",
            "-Dfile.encoding=UTF-8",
        };
        if (!Directory.Exists(nativesDir)) args.Add($"-Djava.library.path={nativesDir}");
        if (!string.IsNullOrWhiteSpace(options.ExtraJvmArgs)) args.Add(options.ExtraJvmArgs);

        if (detail.Arguments is { } templates)
        {
            AppendTemplate(args, templates.Jvm, vars);
            args.Add(detail.MainClass ?? "net.minecraft.client.main.Main");
            AppendTemplate(args, templates.Game, vars);
        }
        else
        {
            // 旧版本兜底(无 arguments 模板)
            args.Add("-cp");
            args.Add(vars["classpath"]);
            args.Add(detail.MainClass ?? "net.minecraft.client.main.Main");
            args.Add("--username"); args.Add(playerName);
            args.Add("--version"); args.Add(detail.Id);
            args.Add("--gameDir"); args.Add(gameRoot);
            args.Add("--assetsDir"); args.Add(assetsDir);
            if (detail.Assets is { Length: > 0 } assets) { args.Add("--assetIndex"); args.Add(assets); }
            args.Add("--uuid"); args.Add(vars["auth_uuid"]);
            args.Add("--accessToken"); args.Add("0");
            args.Add("--userType"); args.Add("legacy");
            args.Add("--versionType"); args.Add("release");
        }

        // 日志配置(1.12+):-Dlog4j.configurationFile=<file>
        if (detail.Logging?.Client is { } logging)
        {
            var logPath = Path.Combine(assetsDir, "log_configs", Path.GetFileName(logging.File?.Path ?? "client.xml"));
            if (File.Exists(logPath) && logging.Argument is { Length: > 0 } logArg)
                args.Insert(0, logArg.Replace("${path}", logPath));
        }

        return args;
    }

    /// 官方模板展开:元素为 string → 替换变量;为对象 → 规则命中后取 value(可数组)
    private static void AppendTemplate(List<string> args, List<JsonElement>? items, Dictionary<string, string> vars)
    {
        if (items == null) return;
        foreach (var item in items)
        {
            switch (item.ValueKind)
            {
                case JsonValueKind.String:
                    args.Add(Expand(item.GetString() ?? "", vars));
                    break;
                case JsonValueKind.Object:
                    if (item.TryGetProperty("rules", out var rulesEl) &&
                        !PassesRules(JsonSerializer.Deserialize<List<RuleEntry>>(rulesEl.GetRawText())))
                        break;
                    if (!item.TryGetProperty("value", out var valueEl)) break;
                    if (valueEl.ValueKind == JsonValueKind.String)
                        args.Add(Expand(valueEl.GetString() ?? "", vars));
                    else if (valueEl.ValueKind == JsonValueKind.Array)
                        foreach (var v in valueEl.EnumerateArray())
                            args.Add(Expand(v.GetString() ?? "", vars));
                    break;
            }
        }
    }

    private static bool PassesRules(List<RuleEntry>? rules) => LibraryResolver.PassesRules(rules);

    private static string Expand(string template, Dictionary<string, string> vars)
    {
        if (!template.Contains("${")) return template;
        var sb = new StringBuilder(template);
        foreach (var (key, value) in vars)
            sb.Replace("${" + key + "}", value);
        return sb.ToString();
    }

    /// 离线模式 UUID:与官方一致的 md5("OfflinePlayer:<name>") 版本 3 UUID
    private static string OfflineUuid(string playerName)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + playerName));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x30); // version 3
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // variant
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }

    private static string Quote(string arg)
        => arg.Contains(' ') ? '"' + arg.Replace("\"", "\\\"") + '"' : arg;
}

public sealed class LaunchOptions
{
    public string? PlayerName { get; set; }
    public int MaxMemoryMb { get; set; } = 4096;
    public string? ExtraJvmArgs { get; set; }
}

public sealed class LaunchPlan
{
    public bool CanLaunch { get; set; }
    public string FileName { get; set; } = "java";
    public string Arguments { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<string> Missing { get; set; } = new();
    public List<string> LibraryMissing { get; set; } = new();
}
