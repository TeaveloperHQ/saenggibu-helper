using System.Text.RegularExpressions;
using ClosedXML.Excel;

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

    /// <summary>학급 시트를 엑셀로 내보내기(학번·이름·내용).</summary>
    public static void WriteXlsx(string path, IEnumerable<(string num, string name, string content)> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("명단");
        ws.Cell(1, 1).Value = "학번"; ws.Cell(1, 2).Value = "이름"; ws.Cell(1, 3).Value = "내용";
        int r = 2;
        foreach (var (num, name, content) in rows)
        {
            ws.Cell(r, 1).Value = num; ws.Cell(r, 2).Value = name; ws.Cell(r, 3).Value = content; r++;
        }
        ws.Columns().AdjustToContents();
        wb.SaveAs(path);
    }

    private static readonly string[] ContentHdr =
        { "특기사항", "세부능력", "행동특성", "종합의견", "내용", "기재", "특기", "의견" };

    private static int? HeuristicCol(List<(int i, string header, double avg)> profs)
    {
        foreach (var p in profs)
            if (ContentHdr.Any(k => p.header.Contains(k, StringComparison.Ordinal)))
                return p.i;
        var cand = profs.Where(p => p.avg > 0).ToList();
        if (cand.Count == 0) return null;
        // Python max(cand, key=avg): 첫 최댓값(동률 시 앞쪽)
        var best = cand[0];
        foreach (var p in cand) if (p.avg > best.avg) best = p;
        return best.i;
    }

    /// <summary>app/importer.py parse_xlsx — 엑셀에서 생기부 본문 열을 휴리스틱으로 추출.</summary>
    public static List<string> ParseXlsx(string path, int minLen = 5)
    {
        var records = new List<string>();
        using var wb = new XLWorkbook(path);
        foreach (var ws in wb.Worksheets)
        {
            var used = ws.RangeUsed();
            if (used == null) continue;
            var rows = used.Rows().ToList();
            if (rows.Count < 2) continue;
            int ncol = rows.Max(r => r.CellCount());
            // 열 프로파일(헤더 = 첫 행, avg = 2행부터 비어있지 않은 셀 평균 길이)
            var profs = new List<(int i, string header, double avg)>();
            for (int c = 0; c < ncol; c++)
            {
                string header = c < rows[0].CellCount() ? (rows[0].Cell(c + 1).GetString() ?? "").Trim() : "";
                var vals = new List<string>();
                for (int r = 1; r < rows.Count; r++)
                {
                    if (c >= rows[r].CellCount()) continue;
                    string v = (rows[r].Cell(c + 1).GetString() ?? "").Trim();
                    if (v.Length > 0) vals.Add(v);
                }
                double avg = vals.Count > 0 ? vals.Average(v => (double)v.Length) : 0;
                profs.Add((c, header, avg));
            }
            int? col = HeuristicCol(profs);
            if (col == null) continue;
            for (int r = 1; r < rows.Count; r++)
            {
                if (col.Value >= rows[r].CellCount()) continue;
                string v = (rows[r].Cell(col.Value + 1).GetString() ?? "").Trim();
                if (v.Length >= minLen) records.Add(v);
            }
        }
        return records;
    }
}
