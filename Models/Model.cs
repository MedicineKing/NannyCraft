using System.Text.Json;
using System.Text.Json.Serialization;

namespace McLauncher.Models;

/// Mojang 版本清单条目(version_manifest_v2.versions[])
public sealed class VersionEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("releaseTime")] public DateTimeOffset ReleaseTime { get; set; }

    public bool IsRelease => Type == "release";
    public string DisplayType => Type switch
    {
        "release" => Services.Loc.T("S_TypeRelease"),
        "snapshot" => Services.Loc.T("S_TypeSnapshot"),
        "old_beta" => Services.Loc.T("S_TypeOldBeta"),
        "old_alpha" => Services.Loc.T("S_TypeOldAlpha"),
        "local" => Services.Loc.T("S_TypeLocal"),
        _ => Type,
    };
}

/// 单版本 JSON(仅取本框架所需字段)
public sealed class VersionDetail
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("mainClass")] public string? MainClass { get; set; }
    [JsonPropertyName("assets")] public string? Assets { get; set; }
    [JsonPropertyName("inheritsFrom")] public string? InheritsFrom { get; set; }
    [JsonPropertyName("javaVersion")] public JavaVersionHint? JavaVersion { get; set; }
    [JsonPropertyName("assetIndex")] public VersionAssetIndex? AssetIndex { get; set; }
    [JsonPropertyName("downloads")] public VersionDownloads? Downloads { get; set; }
    [JsonPropertyName("libraries")] public List<LibraryEntry>? Libraries { get; set; }
    /// 1.13+ 启动参数模板(string 或 {rules,value} 混合数组);旧版本为空 → 走兜底拼装
    [JsonPropertyName("arguments")] public VersionArguments? Arguments { get; set; }
    /// 日志配置(1.12+,log4j 配置文件)
    [JsonPropertyName("logging")] public VersionLogging? Logging { get; set; }
}

/// 启动参数:元素为纯字符串或带规则的对象(规则不通过则跳过)
public sealed class VersionArguments
{
    [JsonPropertyName("game")] public List<JsonElement>? Game { get; set; }
    [JsonPropertyName("jvm")] public List<JsonElement>? Jvm { get; set; }
}

public sealed class VersionLogging
{
    [JsonPropertyName("client")] public VersionLoggingFile? Client { get; set; }
}

public sealed class VersionLoggingFile
{
    /// 形如 "-Dlog4j.configurationFile=${path}"
    [JsonPropertyName("argument")] public string? Argument { get; set; }
    [JsonPropertyName("file")] public DownloadFile? File { get; set; }
}

/// 适用性规则(os / features)
public sealed class RuleEntry
{
    [JsonPropertyName("action")] public string Action { get; set; } = "allow";
    [JsonPropertyName("os")] public OsRule? Os { get; set; }
    [JsonPropertyName("features")] public Dictionary<string, bool>? Features { get; set; }
}

public sealed class OsRule
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("arch")] public string? Arch { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
}

public sealed class ExtractRule
{
    [JsonPropertyName("exclude")] public List<string>? Exclude { get; set; }
}

/// 官方 javaVersion 映射:majorVersion 决定所需 JDK;component 为官方组件名(1.21 系映射 Temurin 等)
public sealed class JavaVersionHint
{
    [JsonPropertyName("component")] public string? Component { get; set; }
    [JsonPropertyName("majorVersion")] public int MajorVersion { get; set; }
}

public sealed class VersionAssetIndex
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("sha1")] public string? Sha1 { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
}

public sealed class VersionDownloads
{
    [JsonPropertyName("client")] public DownloadFile? Client { get; set; }
}

public sealed class DownloadFile
{
    [JsonPropertyName("sha1")] public string? Sha1 { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    /// 仅 libraries.artifact 携带:maven 相对路径(如 net/java/jinput/…jar)
    [JsonPropertyName("path")] public string? Path { get; set; }
}

public sealed class LibraryEntry
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("downloads")] public LibraryDownloads? Downloads { get; set; }
    [JsonPropertyName("rules")] public List<RuleEntry>? Rules { get; set; }
    /// 旧格式 natives:os 名 → classifiers 键(如 windows → natives-windows)
    [JsonPropertyName("natives")] public Dictionary<string, string>? Natives { get; set; }
    [JsonPropertyName("extract")] public ExtractRule? Extract { get; set; }
}

public sealed class LibraryDownloads
{
    [JsonPropertyName("artifact")] public DownloadFile? Artifact { get; set; }
    [JsonPropertyName("classifiers")] public Dictionary<string, DownloadFile>? Classifiers { get; set; }
}

/// 本机探测到的 Java 运行时
public sealed class JavaRuntime
{
    public string Home { get; init; } = "";
    public string JavaExe { get; init; } = "";
    /// 主版本号(21 = JDK 21;0 表示未解析出)
    public int Version { get; init; }
    public JavaSource Source { get; init; }

    public string SourceText => Source switch
    {
        JavaSource.JavaHome => Services.Loc.T("S_SrcJavaHome"),
        JavaSource.Registry => Services.Loc.T("S_SrcRegistry"),
        JavaSource.CommonDir => Services.Loc.T("S_SrcCommonDir"),
        JavaSource.Path => Services.Loc.T("S_SrcPath"),
        JavaSource.Bundled => Services.Loc.T("S_SrcBundled"),
        _ => "?",
    };
}

public enum JavaSource { JavaHome, Registry, CommonDir, Path, Bundled }
