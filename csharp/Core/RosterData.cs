using System.Text.Json;

namespace Saenggibu;

/// <summary>app/roster_data.py 이식 — 학급 명단(roster_*.json) 읽기 + 표시명 파싱.</summary>
public static class RosterData
{
    private static IEnumerable<string> RosterFiles(string dir, string? area) =>
        area != null
            ? new[] { Path.Combine(dir, $"roster_{area}.json") }.Where(File.Exists)
            : Directory.Exists(dir)
                ? Directory.GetFiles(dir, "roster_*.json").OrderBy(f => f, StringComparer.Ordinal)
                : Enumerable.Empty<string>();

    private static (string num, string name) RowKey(JsonElement row)
    {
        string num = row.GetArrayLength() > 0 ? (row[0].GetString() ?? "").Trim() : "";
        string name = row.GetArrayLength() > 1 ? (row[1].GetString() ?? "").Trim() : "";
        return (num, name);
    }

    /// <summary>app/roster_data.py classes_and_students — {학급: [표시명]}. area=null이면 전 영역 병합.</summary>
    public static Dictionary<string, List<string>> ClassesAndStudents(string dir, string? area = null)
    {
        // klass -> 삽입순 유니크 (num,name)
        var merged = new Dictionary<string, List<(string num, string name)>>();
        var seenPer = new Dictionary<string, HashSet<(string, string)>>();
        foreach (var f in RosterFiles(dir, area))
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(File.ReadAllText(f)); }
            catch { continue; }
            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                foreach (var kv in doc.RootElement.EnumerateObject())
                {
                    if (kv.Value.ValueKind != JsonValueKind.Object
                        || !kv.Value.TryGetProperty("rows", out var rows)
                        || rows.ValueKind != JsonValueKind.Array) continue;
                    if (!merged.TryGetValue(kv.Name, out var bag))
                    { bag = new(); merged[kv.Name] = bag; seenPer[kv.Name] = new(); }
                    foreach (var row in rows.EnumerateArray())
                    {
                        if (row.ValueKind != JsonValueKind.Array) continue;
                        var (num, name) = RowKey(row);
                        if ((num.Length > 0 || name.Length > 0) && seenPer[kv.Name].Add((num, name)))
                            bag.Add((num, name));
                    }
                }
            }
        }
        var outp = new Dictionary<string, List<string>>();
        foreach (var klass in merged.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var studs = merged[klass]
                .OrderBy(t => t.num.Length > 0 ? t.num : "999", StringComparer.Ordinal)
                .ThenBy(t => t.name, StringComparer.Ordinal)
                .Where(t => t.num.Length > 0 || t.name.Length > 0);
            outp[klass] = studs.Select(t => t.num.Length > 0 ? $"{t.num} {t.name}".Trim() : t.name).ToList();
        }
        return outp;
    }

    /// <summary>app/roster_data.py roster_records — 그 영역 등록 학생 레코드.</summary>
    public static List<(string klass, string num, string name)> RosterRecords(string dir, string area)
    {
        var path = Path.Combine(dir, $"roster_{area}.json");
        if (!File.Exists(path)) return new();
        JsonDocument doc;
        try { doc = JsonDocument.Parse(File.ReadAllText(path)); }
        catch { return new(); }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return new();
            var outp = new List<(string, string, string)>();
            var seen = new HashSet<(string, string, string)>();
            foreach (var kv in doc.RootElement.EnumerateObject())
            {
                if (kv.Value.ValueKind != JsonValueKind.Object
                    || !kv.Value.TryGetProperty("rows", out var rows)
                    || rows.ValueKind != JsonValueKind.Array) continue;
                foreach (var row in rows.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Array) continue;
                    var (num, name) = RowKey(row);
                    if (num.Length == 0 && name.Length == 0) continue;
                    var key = (kv.Name, num, name);
                    if (seen.Add(key)) outp.Add(key);
                }
            }
            return outp;
        }
    }

    /// <summary>'학번 이름' 표시명 → (학번, 이름). 학번 없으면 ("", 이름).</summary>
    public static (string num, string name) ParseStudentLabel(string? label)
    {
        string s = (label ?? "").Trim();
        if (s.Length == 0) return ("", "");
        // Python str.split(None, 1): 첫 공백에서 2조각(뒷조각 leading 공백 제거)
        int i = 0;
        while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;
        if (i >= s.Length) return ("", s);          // 공백 없음
        string first = s[..i];
        string rest = s[i..].TrimStart();
        if (first.Length > 0 && first.All(char.IsDigit))
            return (first, rest);
        return ("", s);
    }
}
