using System.Text.RegularExpressions;

namespace Saenggibu;

/// <summary>
/// app/importer.py 이식 중 결정론·비IO 부분(extract_keywords, parse_records).
/// 엑셀/CSV 파싱(ClosedXML)·import_records(store+compliance)는 후속.
/// </summary>
public static class Importer
{
    private static readonly HashSet<string> Stop = new()
    {
        "학생", "활동", "모습", "태도", "자세", "과정", "내용", "수업", "시간", "부분",
        "생각", "경우", "정도", "대해", "통해", "위해", "다양", "여러", "자신", "매우",
        "항상", "특히", "또한", "그리고", "이를", "관련", "다른", "함께", "스스로",
    };
    private static readonly string[] TailLong =
        { "하였으며", "하면서", "하였고", "하며", "하고", "으로", "에서", "이며",
          "하는", "한다", "였으며", "되어", "되며" };
    private static readonly string[] TailShort =
        { "함", "임", "됨", "음", "며", "고", "을", "를", "이", "가", "은", "는",
          "과", "와", "에", "의", "도", "만" };

    private static readonly Regex Word2 = new(@"[가-힣]{2,}", RegexOptions.Compiled);

    public static string ExtractKeywords(string text, int maxK = 6)
    {
        var cleaned = new List<string>();
        foreach (Match m in Word2.Matches(text))
        {
            string w = m.Value;
            foreach (var suf in TailLong)
                if (w.EndsWith(suf, StringComparison.Ordinal) && w.Length > suf.Length)
                { w = w[..^suf.Length]; break; }
            foreach (var suf in TailShort)
                if (w.EndsWith(suf, StringComparison.Ordinal) && w.Length > 2)
                { w = w[..^suf.Length]; break; }
            if (w.Length >= 2 && !Stop.Contains(w))
                cleaned.Add(w);
        }
        if (cleaned.Count == 0)
            return text.Length <= 30 ? text : text[..30];

        // Counter.most_common(maxK): 빈도 내림차순, 동률은 first-seen 순(안정)
        var count = new Dictionary<string, int>();
        var firstSeen = new Dictionary<string, int>();
        int order = 0;
        foreach (var w in cleaned)
        {
            if (!count.ContainsKey(w)) { count[w] = 0; firstSeen[w] = order++; }
            count[w]++;
        }
        var top = count.Keys
            .OrderByDescending(w => count[w])
            .ThenBy(w => firstSeen[w])
            .Take(maxK);
        return string.Join(" / ", top);
    }

    private static readonly Regex BlankLine = new(@"\n\s*\n", RegexOptions.Compiled);

    public static List<string> ParseRecords(string text, string mode = "auto", int minLen = 5)
    {
        text = (text ?? "").Replace("\r\n", "\n").Trim();
        if (text.Length == 0) return new List<string>();
        string[] chunks = (mode == "para" || (mode == "auto" && BlankLine.IsMatch(text)))
            ? BlankLine.Split(text)
            : text.Split('\n');
        return chunks.Select(c => c.Trim()).Where(c => c.Length >= minLen).ToList();
    }
}
