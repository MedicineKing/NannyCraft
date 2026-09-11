using System.IO;

namespace McLauncher.Services;

/// options.txt 读写:保留行顺序与未知行(注释/模组/新版字段原样带回)。
/// 值里的冒号(如某些字段)按"首个冒号"切分,保证不破坏内容。
public sealed class GameOptionsFile
{
    private readonly List<string> _lines = new();

    public string Path { get; }
    public bool Exists { get; private set; }

    public GameOptionsFile(string path)
    {
        Path = path;
        if (File.Exists(path))
        {
            Exists = true;
            _lines.AddRange(File.ReadAllLines(path));
        }
    }

    /// 查询(大小写不敏感;返回原始值或 null)
    public string? Get(string key)
    {
        var prefix = key + ":";
        foreach (var line in _lines)
        {
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return line[prefix.Length..];
        }
        return null;
    }

    public bool Has(string key) => Get(key) != null;

    /// 设置(不存在则追加到末尾)
    public void Set(string key, string value)
    {
        var prefix = key + ":";
        for (var i = 0; i < _lines.Count; i++)
        {
            if (!_lines[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            _lines[i] = prefix + value;
            return;
        }
        _lines.Add(prefix + value);
    }

    /// 全部 key_ 开头的键位(action → 值)
    public IEnumerable<(string Action, string Value)> Keybinds()
    {
        foreach (var line in _lines)
        {
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;
            var key = line[..idx];
            if (!key.StartsWith("key_", StringComparison.OrdinalIgnoreCase)) continue;
            yield return (key[4..], line[(idx + 1)..]);
        }
    }

    /// 键位值是否为旧版数字键码(1.12 及以前)
    public bool UsesLegacyKeyCodes()
    {
        foreach (var (_, value) in Keybinds())
        {
            if (int.TryParse(value, out _)) continue;
            if (value.StartsWith("key.", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("button.", StringComparison.OrdinalIgnoreCase)) return false;
        }
        return Keybinds().Any(); // 全是数字(或空文件) → 按旧格式处理
    }

    public void Save()
    {
        var dir = System.IO.Path.GetDirectoryName(Path);
        if (dir is { Length: > 0 }) Directory.CreateDirectory(dir);
        File.WriteAllLines(Path, _lines);
        Exists = true;
    }
}
