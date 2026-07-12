using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

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

    /// <summary>영역 로스터의 학급 이름 목록.</summary>
    public static List<string> ClassNames(string dir, string area)
    {
        var path = Path.Combine(dir, $"roster_{area}.json");
        if (!File.Exists(path)) return new();
        try { return (JsonNode.Parse(File.ReadAllText(path)) as JsonObject)?.Select(kv => kv.Key).ToList() ?? new(); }
        catch { return new(); }
    }

    /// <summary>학급 이름 변경(JSON 키 교체, 순서 유지). 성공 시 true.</summary>
    public static bool RenameClass(string dir, string area, string oldName, string newName)
    {
        newName = (newName ?? "").Trim();
        if (newName.Length == 0 || oldName == newName) return false;
        var path = Path.Combine(dir, $"roster_{area}.json");
        if (!File.Exists(path)) return false;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject o || o[oldName] is not JsonNode entry) return false;
            if (o.ContainsKey(newName)) return false;   // 중복 이름 금지
            var rebuilt = new JsonObject();             // 순서 유지하며 키만 교체
            foreach (var kv in o) rebuilt[kv.Key == oldName ? newName : kv.Key] = kv.Value!.DeepClone();
            File.WriteAllText(path, rebuilt.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            return true;
        }
        catch { return false; }
    }

    /// <summary>학급 시트 행 읽기 → (학번, 이름, 내용) 리스트.</summary>
    public static List<(string num, string name, string content)> ReadRows(string dir, string area, string klass)
    {
        var outp = new List<(string, string, string)>();
        var path = Path.Combine(dir, $"roster_{area}.json");
        if (!File.Exists(path)) return outp;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject o && o[klass] is JsonObject e && e["rows"] is JsonArray rows)
                foreach (var r in rows)
                    if (r is JsonArray row)
                        outp.Add((row.Count > 0 ? row[0]?.GetValue<string>() ?? "" : "",
                                  row.Count > 1 ? row[1]?.GetValue<string>() ?? "" : "",
                                  row.Count > 2 ? row[2]?.GetValue<string>() ?? "" : ""));
        }
        catch { }
        return outp;
    }

    /// <summary>학급 시트 저장(학번·이름·내용). 빈 행 제외.</summary>
    public static void WriteRows(string dir, string area, string klass, IEnumerable<(string num, string name, string content)> rows)
    {
        var path = Path.Combine(dir, $"roster_{area}.json");
        JsonObject data;
        try { data = (JsonNode.Parse(File.Exists(path) ? File.ReadAllText(path) : "{}") as JsonObject) ?? new(); }
        catch { data = new(); }
        var arr = new JsonArray();
        foreach (var (num, name, content) in rows)
            if (!(string.IsNullOrWhiteSpace(num) && string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(content)))
                arr.Add(new JsonArray(num, name, content));
        data[klass] = new JsonObject { ["headers"] = new JsonArray("내용"), ["rows"] = arr };
        File.WriteAllText(path, data.ToJsonString(new JsonSerializerOptions
        { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    /// <summary>확장 시트 읽기 — 첫 내용 열 라벨(contentLabel) + 추가 내용 열(ext) + 각 행의 추가 값.</summary>
    public static (string contentLabel, List<(string id, string label)> extra,
                   List<(string num, string name, string content, List<string> extraVals)> rows)
        ReadRowsExtended(string dir, string area, string klass)
    {
        string contentLabel = "내용";
        var extra = new List<(string, string)>();
        var outp = new List<(string, string, string, List<string>)>();
        var path = Path.Combine(dir, $"roster_{area}.json");
        if (!File.Exists(path)) return (contentLabel, extra, outp);
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject o && o[klass] is JsonObject e)
            {
                if (e["headers"] is JsonArray hs && hs.Count > 0 && hs[0]?.GetValue<string>() is { Length: > 0 } h0) contentLabel = h0;
                if (e["ext"] is JsonArray ea)
                    foreach (var it in ea)
                        if (it is JsonObject eo)
                            extra.Add((eo["id"]?.GetValue<string>() ?? "", eo["label"]?.GetValue<string>() ?? ""));
                if (e["rows"] is JsonArray rows)
                    foreach (var r in rows)
                        if (r is JsonArray row)
                        {
                            string num = row.Count > 0 ? row[0]?.GetValue<string>() ?? "" : "";
                            string name = row.Count > 1 ? row[1]?.GetValue<string>() ?? "" : "";
                            string content = row.Count > 2 ? row[2]?.GetValue<string>() ?? "" : "";
                            var ev = new List<string>();
                            for (int i = 0; i < extra.Count; i++)
                                ev.Add(row.Count > 3 + i ? row[3 + i]?.GetValue<string>() ?? "" : "");
                            outp.Add((num, name, content, ev));
                        }
            }
        }
        catch { }
        return (contentLabel, extra, outp);
    }

    /// <summary>확장 시트 저장 — 학번·이름 + 내용 열들(contentLabel 첫 열) + 각 행 값. 완전 빈 행 제외.</summary>
    public static void WriteRowsExtended(string dir, string area, string klass, string contentLabel,
        IReadOnlyList<(string id, string label)> extra,
        IEnumerable<(string num, string name, string content, IReadOnlyList<string> extraVals)> rows)
    {
        var path = Path.Combine(dir, $"roster_{area}.json");
        JsonObject data;
        try { data = (JsonNode.Parse(File.Exists(path) ? File.ReadAllText(path) : "{}") as JsonObject) ?? new(); }
        catch { data = new(); }
        var arr = new JsonArray();
        foreach (var (num, name, content, ev) in rows)
        {
            bool empty = string.IsNullOrWhiteSpace(num) && string.IsNullOrWhiteSpace(name)
                         && string.IsNullOrWhiteSpace(content) && (ev == null || ev.All(string.IsNullOrWhiteSpace));
            if (empty) continue;
            var row = new JsonArray(num, name, content);
            if (ev != null) foreach (var v in ev) row.Add(v ?? "");
            arr.Add(row);
        }
        var ext = new JsonArray();
        foreach (var (id, label) in extra) ext.Add(new JsonObject { ["id"] = id, ["label"] = label });
        data[klass] = new JsonObject { ["headers"] = new JsonArray(string.IsNullOrEmpty(contentLabel) ? "내용" : contentLabel), ["ext"] = ext, ["rows"] = arr };
        File.WriteAllText(path, data.ToJsonString(new JsonSerializerOptions
        { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    /// <summary>시트 보기 상태(열 고정·숨김·너비·행높이) 읽기. 없으면 기본값.</summary>
    public static (int frozen, HashSet<int> hidden, List<double> colWidths, Dictionary<int, double> rowHeights)
        ReadSheetView(string dir, string area, string klass)
    {
        int frozen = 0; var hidden = new HashSet<int>(); var widths = new List<double>(); var rowH = new Dictionary<int, double>();
        var path = Path.Combine(dir, $"roster_{area}.json");
        if (!File.Exists(path)) return (frozen, hidden, widths, rowH);
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject o && o[klass] is JsonObject e && e["view"] is JsonObject v)
            {
                if (v["frozen"] is JsonNode fn) frozen = fn.GetValue<int>();
                if (v["hidden"] is JsonArray ha) foreach (var n in ha) if (n != null) hidden.Add(n.GetValue<int>());
                if (v["colw"] is JsonArray wa) foreach (var n in wa) widths.Add(n?.GetValue<double>() ?? 0);
                if (v["rowh"] is JsonObject ro) foreach (var kv in ro) if (int.TryParse(kv.Key, out var ri) && kv.Value != null) rowH[ri] = kv.Value.GetValue<double>();
            }
        }
        catch { }
        return (frozen, hidden, widths, rowH);
    }

    /// <summary>시트 보기 상태 저장(기존 학급 항목의 'view' 키만 갱신). 학급 항목이 없으면 무시.</summary>
    public static void WriteSheetView(string dir, string area, string klass, int frozen,
        IEnumerable<int> hidden, IEnumerable<double> colWidths, IReadOnlyDictionary<int, double> rowHeights)
    {
        var path = Path.Combine(dir, $"roster_{area}.json");
        if (!File.Exists(path)) return;
        JsonObject data;
        try { if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject o) return; else data = o; }
        catch { return; }
        if (data[klass] is not JsonObject entry) return;
        var view = new JsonObject { ["frozen"] = frozen };
        var ha = new JsonArray(); foreach (var i in hidden) ha.Add(i); view["hidden"] = ha;
        var wa = new JsonArray(); foreach (var w in colWidths) wa.Add(w); view["colw"] = wa;
        var ro = new JsonObject(); foreach (var kv in rowHeights) ro[kv.Key.ToString()] = kv.Value; view["rowh"] = ro;
        entry["view"] = view;
        File.WriteAllText(path, data.ToJsonString(new JsonSerializerOptions
        { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    /// <summary>app/roster_data.py add_memo_to_roster — 메모를 명단에 반영(등록 학급만).
    /// 학생 있으면 내용 이어붙임('append'), 없으면 행 삽입('insert'). 등록 안 된 학급='no_class'.</summary>
    public static string AddMemoToRoster(string dir, string area, string klass, string num, string name, string text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0 || klass.Length == 0 || (num.Length == 0 && name.Length == 0)) return "";
        var path = Path.Combine(dir, $"roster_{area}.json");
        JsonObject data;
        try { data = (JsonNode.Parse(File.Exists(path) ? File.ReadAllText(path) : "{}") as JsonObject) ?? new(); }
        catch { data = new(); }
        if (data[klass] is not JsonObject entry) return "no_class";   // 등록 안 된 학급 → 생성 금지
        if (entry["headers"] is not JsonArray headers) { headers = new JsonArray("내용"); entry["headers"] = headers; }
        if (entry["rows"] is not JsonArray rows) { rows = new JsonArray(); entry["rows"] = rows; }

        JsonArray? target = null;
        foreach (var r in rows)
        {
            if (r is not JsonArray row) continue;
            string rnum = row.Count > 0 ? (row[0]?.GetValue<string>() ?? "").Trim() : "";
            string rname = row.Count > 1 ? (row[1]?.GetValue<string>() ?? "").Trim() : "";
            if (num.Length > 0 && rnum == num) { target = row; break; }
            if (num.Length == 0 && name.Length > 0 && rname == name) { target = row; break; }
        }
        string result;
        if (target == null)
        {
            var nr = new JsonArray(num, name);
            for (int i = 0; i < headers.Count; i++) nr.Add("");
            nr[2] = text;
            rows.Add(nr); result = "insert";
        }
        else
        {
            while (target.Count < 3) target.Add("");
            string cur = (target[2]?.GetValue<string>() ?? "").Trim();
            target[2] = cur.Length > 0 ? $"{cur}\n{text}" : text; result = "append";
        }
        try
        {
            File.WriteAllText(path, data.ToJsonString(new JsonSerializerOptions
            { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        }
        catch { return ""; }
        return result;
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
