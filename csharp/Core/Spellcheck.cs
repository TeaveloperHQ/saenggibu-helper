using System.Text.Json;
using System.Text.RegularExpressions;


namespace Saenggibu;

/// <summary>
/// app/spellcheck.py 의 네이버 맞춤법 교정 이식(온라인, 동의 시에만). urllib → HttpClient.
/// 형태소 분석(오프라인)은 KiwiNative가 담당.
/// </summary>
public static class Spellcheck
{
    private static readonly HttpClient Http = MakeClient();
    private static string? _cachedKey;

    private static HttpClient MakeClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        c.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
        c.DefaultRequestHeaders.Add("Referer", "https://search.naver.com/");
        return c;
    }

    private static string Get(string url) => Http.GetStringAsync(url).GetAwaiter().GetResult();

    private static string? PassportKey(bool refresh)
    {
        if (!refresh && _cachedKey != null) return _cachedKey;
        string html;
        try { html = Get("https://search.naver.com/search.naver?query=" + Uri.EscapeDataString("맞춤법검사기")); }
        catch { return null; }
        var m = Regex.Match(html, "passportKey=([a-zA-Z0-9]+)");
        if (!m.Success) m = Regex.Match(html, "\"passportKey\"\\s*:\\s*\"([^\"]+)\"");
        _cachedKey = m.Success ? m.Groups[1].Value : null;
        return _cachedKey;
    }

    /// <summary>네이버 맞춤법 교정 (교정문, 오류수, html). 온라인 실패 시 null.</summary>
    public static (string corrected, int errata, string html)? NaverSpellcheck(string text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return ("", 0, "");
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var key = PassportKey(attempt == 1);
            if (key == null) return null;
            try
            {
                string url = "https://m.search.naver.com/p/csearch/ocontent/util/SpellerProxy" +
                    $"?passportKey={Uri.EscapeDataString(key)}&where=nexearch&color_blindness=0&q={Uri.EscapeDataString(text)}";
                using var doc = JsonDocument.Parse(Get(url));
                var res = doc.RootElement.GetProperty("message").GetProperty("result");
                string html = res.GetProperty("html").GetString() ?? "";
                string corrected = Regex.Replace(html, "<[^>]+>", "");
                int errata = res.TryGetProperty("errata_count", out var e)
                    ? (e.ValueKind == JsonValueKind.Number ? e.GetInt32() : int.TryParse(e.GetString(), out var n) ? n : 0) : 0;
                return (corrected, errata, html);
            }
            catch { }
        }
        return null;
    }

    /// <summary>네이버 교정 마크업 → 색상 html(빨강=맞춤법, 파랑=띄어쓰기).</summary>
    public static string StyledHtml(string naverHtml)
    {
        string h = naverHtml
            .Replace("<em class='red_text'>", "<span style='color:#d32f2f;font-weight:bold'>")
            .Replace("<em class='green_text'>", "<span style='color:#1565c0;font-weight:bold'>");
        h = Regex.Replace(h, "<em class='[^']*'>", "<span style='color:#e65100;font-weight:bold'>");
        return h.Replace("</em>", "</span>");
    }
}
