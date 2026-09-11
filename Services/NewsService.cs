using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace McLauncher.Services;

/// 一条 MC 快讯(来自官方启动器同源的补丁说明源)
public sealed class NewsItem
{
    public string Version { get; set; } = "";
    public string Title { get; set; } = "";
    public string Date { get; set; } = "";
    public string Summary { get; set; } = "";
}

/// 快讯服务:默认拉官方 javaPatchNotes(与官方启动器「更新内容」同源)。
/// 支持三种中文化/可达性方案(用户可在设置里组合):
///   1) 自定义资讯源地址(如本机转发/镜像,返回同结构 JSON);
///   2) HTTP 代理(如 http://127.0.0.1:7890,请求与翻译都走它);
///   3) 自动翻译(英文 → 简体中文,翻译请求走同一代理)。
/// 任何环节失败都静默降级——离线不该影响启动器主流程。
public sealed class NewsService
{
    public const string OfficialUrl = "https://launchercontent.mojang.com/v2/javaPatchNotes.json";
    private const string TranslateUrl = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl=zh-CN&dt=t";

    private static readonly HttpClient Direct = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static HttpClient BuildClient(string proxy)
    {
        if (string.IsNullOrWhiteSpace(proxy)) return Direct;
        try
        {
            var handler = new HttpClientHandler
            {
                Proxy = new WebProxy(proxy.Trim(), false),
                UseProxy = true,
            };
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        }
        catch { return Direct; } // 代理地址不合法 → 直连
    }

    public async Task<List<NewsItem>> GetLatestAsync(int count, string sourceUrl, string proxy, bool translate, CancellationToken ct = default)
    {
        var list = new List<NewsItem>();
        try
        {
            var http = BuildClient(proxy);
            var url = string.IsNullOrWhiteSpace(sourceUrl) ? OfficialUrl : sourceUrl.Trim();
            var json = await http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("entries", out var entries)) return list;

            foreach (var e in entries.EnumerateArray().Take(count))
            {
                var date = e.TryGetProperty("date", out var d) ? d.GetString() ?? "" : "";
                if (DateTimeOffset.TryParse(date, out var parsed))
                    date = parsed.ToLocalTime().ToString("yyyy-MM-dd");

                list.Add(new NewsItem
                {
                    Version = e.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "",
                    Title = e.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                    Date = date,
                    Summary = e.TryGetProperty("shortText", out var s) ? s.GetString() ?? "" : "",
                });
            }

            if (translate && list.Count > 0)
                await TranslateAsync(http, list, ct);
        }
        catch { /* 离线/源不可达/接口变更 → 无快讯 */ }
        return list;
    }

    private static async Task TranslateAsync(HttpClient http, List<NewsItem> items, CancellationToken ct)
    {
        var tasks = items.Select(async item =>
        {
            var title = await TranslateOneAsync(http, item.Title, ct);
            if (!string.IsNullOrWhiteSpace(title)) item.Title = title;

            var summary = item.Summary.Length > 300 ? item.Summary[..300] : item.Summary;
            var translated = await TranslateOneAsync(http, summary, ct);
            if (!string.IsNullOrWhiteSpace(translated)) item.Summary = translated;
        });
        try { await Task.WhenAll(tasks); } catch { /* 单条失败保留原文 */ }
    }

    private static async Task<string?> TranslateOneAsync(HttpClient http, string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["q"] = text });
            using var response = await http.PostAsync(TranslateUrl, content, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder();
            foreach (var seg in doc.RootElement[0].EnumerateArray())
            {
                if (seg.ValueKind == JsonValueKind.Array && seg.GetArrayLength() > 0)
                    sb.Append(seg[0].GetString());
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch { return null; }
    }
}
