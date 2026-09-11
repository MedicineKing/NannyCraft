using System.IO;

namespace McLauncher.Services;

/// 轻量日志:追加写 %APPDATA%\.mc-launcher\logs\latest.log;崩溃单独落 crash-*.log。
/// 反馈时由 FeedbackService 自动截取尾部内容一并附上。
public static class LogService
{
    private static readonly object Gate = new();
    private const long MaxSize = 1024 * 1024; // 1MB 轮换

    public static string LogDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".mc-launcher", "logs");

    public static string LatestPath { get; } = Path.Combine(LogDir, "latest.log");

    public static string? LastCrashPath { get; private set; }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex == null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    public static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDir);
                if (File.Exists(LatestPath) && new FileInfo(LatestPath).Length > MaxSize)
                    File.Delete(LatestPath);
                File.AppendAllText(LatestPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}");
            }
        }
        catch { /* 日志失败绝不影响主流程 */ }
    }

    /// 未处理异常:独立崩溃文件 + 记录当前日志路径,供反馈自动附带
    public static void WriteCrash(Exception ex, string context)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            var path = Path.Combine(LogDir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{ex}");
            LastCrashPath = path;
            Write("CRASH", $"{context}({ex.GetType().Name}: {ex.Message}) → {path}");
        }
        catch { }
    }

    /// latest.log 尾部(maxLines 行、maxChars 字符封顶)
    public static string Tail(int maxLines = 80, int maxChars = 5000)
    {
        try
        {
            if (!File.Exists(LatestPath)) return "(无日志)";
            var lines = File.ReadAllLines(LatestPath);
            var tail = string.Join(Environment.NewLine, lines.TakeLast(maxLines));
            return tail.Length > maxChars ? tail[^maxChars..] : tail;
        }
        catch { return "(日志读取失败)"; }
    }

    /// 最近的崩溃日志内容(无则 null)
    public static string? CrashTail(int maxChars = 5000)
    {
        try
        {
            var path = LastCrashPath;
            if (path == null || !File.Exists(path))
            {
                // 本次会话没有崩溃 → 找目录里最新的一份(上次运行的崩溃也值得带上)
                if (!Directory.Exists(LogDir)) return null;
                path = Directory.GetFiles(LogDir, "crash-*.log").OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
            }
            if (path == null) return null;
            var text = File.ReadAllText(path);
            return text.Length > maxChars ? text[^maxChars..] : text;
        }
        catch { return null; }
    }
}
