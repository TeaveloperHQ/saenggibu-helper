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

    private static readonly string[] ContentKeys = { "특기사항", "세부능력", "행동특성", "종합의견", "내용", "기재", "특기", "의견" };

    private static string Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : n?.ToString() ?? "";

    /// <summary>학급 항목의 열 라벨(행 값 순서와 같음: corehdr 2개 → headers[0] → ext 라벨).</summary>
    private static List<string> Labels(JsonObject entry)
    {
        var ch = entry["corehdr"] as JsonArray;
        var hs = entry["headers"] as JsonArray;
        string Or(string s, string d) => s.Length > 0 ? s : d;
        var labels = new List<string>
        {
            Or(Str(ch?.ElementAtOrDefault(0)), "학번"), Or(Str(ch?.ElementAtOrDefault(1)), "이름"), Or(Str(hs?.ElementAtOrDefault(0)), "내용"),
        };
        if (entry["ext"] is JsonArray ea) foreach (var it in ea) labels.Add(Str((it as JsonObject)?["label"]));
        else if (hs != null) labels.AddRange(hs.Skip(1).Select(Str));   // 옛 형식: headers = 내용 열들
        return labels;
    }

    /// <summary>번호·이름·내용 열 위치. 엑셀에서 가져온 시트는 열 순서가 제각각이라
    /// (예: 학년도|학기|학년|반/번호|학생개인번호|성명|과목명|세부능력…) 1·2·3열 고정 대신 라벨로 찾는다.</summary>
    private static (int num, int name, int content) KeyCols(List<string> labels)
    {
        int Find(Func<string, bool> pred, params int[] skip)
        {
            for (int i = 0; i < labels.Count; i++) if (!skip.Contains(i) && pred(labels[i])) return i;
            return -1;
        }
        int name = Find(l => l.Contains("이름") || l.Contains("성명"));
        int num = Find(l => l is "번호" or "학번", name);
        if (num < 0) num = Find(l => l.Contains("번호") && !l.Contains("개인"), name);   // '반/번호' O, '학생개인번호' X
        if (num < 0) num = Find(l => l.Contains("학번"), name);
        if (num < 0) num = name == 0 ? 1 : 0;
        if (name < 0) name = num == 1 ? 0 : 1;
        int content = Find(l => ContentKeys.Any(k => l.Contains(k)), num, name);
        if (content < 0) content = Enumerable.Range(2, labels.Count + 2).First(i => i != num && i != name);
        return (num, name, content);
    }

    /// <summary>'반/번호' 값(예: 1/3) → 번호만.</summary>
    private static string NumVal(string v) { v = v.Trim(); int s = v.LastIndexOf('/'); return s >= 0 ? v[(s + 1)..].Trim() : v; }

    private static (string num, string name) RowKey(JsonArray row, (int num, int name, int content) cols) =>
        (NumVal(Str(row.ElementAtOrDefault(cols.num))), Str(row.ElementAtOrDefault(cols.name)).Trim());

    /// <summary>로스터 파일의 (학급, 항목) 목록. 읽기 실패·형식 불일치는 건너뜀.</summary>
    private static IEnumerable<(string klass, JsonObject entry, JsonArray rows)> Sheets(string path)
    {
        JsonObject? o;
        try { o = JsonNode.Parse(File.ReadAllText(path)) as JsonObject; }
        catch { yield break; }
        if (o == null) yield break;
        foreach (var kv in o)
            if (kv.Value is JsonObject e && e["rows"] is JsonArray rows) yield return (kv.Key, e, rows);
    }

    /// <summary>app/roster_data.py classes_and_students — {학급: [표시명]}. area=null이면 전 영역 병합.</summary>
    public static Dictionary<string, List<string>> ClassesAndStudents(string dir, string? area = null)
    {
        // klass -> 삽입순 유니크 (num,name)
        var merged = new Dictionary<string, List<(string num, string name)>>();
        var seenPer = new Dictionary<string, HashSet<(string, string)>>();
        foreach (var f in RosterFiles(dir, area))
            foreach (var (klass, entry, rows) in Sheets(f))
            {
                if (!merged.TryGetValue(klass, out var bag))
                { bag = new(); merged[klass] = bag; seenPer[klass] = new(); }
                var cols = KeyCols(Labels(entry));
                foreach (var r in rows)
                {
                    if (r is not JsonArray row) continue;
                    var (num, name) = RowKey(row, cols);
                    if ((num.Length > 0 || name.Length > 0) && seenPer[klass].Add((num, name)))
                        bag.Add((num, name));
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

    /// <summary>확장 시트 읽기 — 코어 열 라벨(학번·이름·내용) + 추가 내용 열(ext) + 각 행의 추가 값.</summary>
    public static (string numLabel, string nameLabel, string contentLabel, List<(string id, string label)> extra,
                   List<(string num, string name, string content, List<string> extraVals)> rows)
        ReadRowsExtended(string dir, string area, string klass)
    {
        string numLabel = "학번", nameLabel = "이름", contentLabel = "내용";
        var extra = new List<(string, string)>();
        var outp = new List<(string, string, string, List<string>)>();
        var path = Path.Combine(dir, $"roster_{area}.json");
        if (!File.Exists(path)) return (numLabel, nameLabel, contentLabel, extra, outp);
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject o && o[klass] is JsonObject e)
            {
                if (e["headers"] is JsonArray hs && hs.Count > 0 && hs[0]?.GetValue<string>() is { Length: > 0 } h0) contentLabel = h0;
                if (e["corehdr"] is JsonArray ch)
                {
                    if (ch.Count > 0 && ch[0]?.GetValue<string>() is { Length: > 0 } cn) numLabel = cn;
                    if (ch.Count > 1 && ch[1]?.GetValue<string>() is { Length: > 0 } cm) nameLabel = cm;
                }
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
        return (numLabel, nameLabel, contentLabel, extra, outp);
    }

    /// <summary>확장 시트 저장 — 코어 열 라벨(학번·이름·내용) + 추가 내용 열 + 각 행 값. 완전 빈 행 제외.</summary>
    public static void WriteRowsExtended(string dir, string area, string klass,
        string numLabel, string nameLabel, string contentLabel,
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
        var keepView = (data[klass] as JsonObject)?["view"]?.DeepClone();   // 보기 상태(열 너비·행 높이 등) 보존
        var entry = new JsonObject
        {
            ["headers"] = new JsonArray(string.IsNullOrEmpty(contentLabel) ? "내용" : contentLabel),
            ["corehdr"] = new JsonArray(string.IsNullOrEmpty(numLabel) ? "학번" : numLabel, string.IsNullOrEmpty(nameLabel) ? "이름" : nameLabel),
            ["ext"] = ext, ["rows"] = arr,
        };
        if (keepView != null) entry["view"] = keepView;
        data[klass] = entry;
        File.WriteAllText(path, data.ToJsonString(new JsonSerializerOptions
        { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    /// <summary>시트 보기 상태(열 고정·숨김·너비·행높이) 읽기. 없으면 기본값.</summary>
    public static (int frozen, HashSet<int> hidden, List<double> colWidths, Dictionary<int, double> rowHeights, bool contentFill)
        ReadSheetView(string dir, string area, string klass)
    {
        int frozen = 0; var hidden = new HashSet<int>(); var widths = new List<double>(); var rowH = new Dictionary<int, double>();
        bool fill = true;   // 내용 열 남는 폭 채우기(기본). 사용자가 직접 조절하면 false
        var path = Path.Combine(dir, $"roster_{area}.json");
        if (!File.Exists(path)) return (frozen, hidden, widths, rowH, fill);
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject o && o[klass] is JsonObject e && e["view"] is JsonObject v)
            {
                if (v["frozen"] is JsonNode fn) frozen = fn.GetValue<int>();
                if (v["hidden"] is JsonArray ha) foreach (var n in ha) if (n != null) hidden.Add(n.GetValue<int>());
                if (v["colw"] is JsonArray wa) foreach (var n in wa) widths.Add(n?.GetValue<double>() ?? 0);
                if (v["rowh"] is JsonObject ro) foreach (var kv in ro) if (int.TryParse(kv.Key, out var ri) && kv.Value != null) rowH[ri] = kv.Value.GetValue<double>();
                if (v["cfill"] is JsonNode fl) fill = fl.GetValue<bool>();   // 'fill'(초기 오판 저장값)은 무시
            }
        }
        catch { }
        return (frozen, hidden, widths, rowH, fill);
    }

    /// <summary>시트 보기 상태 저장(기존 학급 항목의 'view' 키만 갱신). 학급 항목이 없으면 무시.</summary>
    public static void WriteSheetView(string dir, string area, string klass, int frozen,
        IEnumerable<int> hidden, IEnumerable<double> colWidths, IReadOnlyDictionary<int, double> rowHeights, bool contentFill = true)
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
        view["cfill"] = contentFill;
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
        if (entry["rows"] is not JsonArray rows) { rows = new JsonArray(); entry["rows"] = rows; }
        var labels = Labels(entry);
        var cols = KeyCols(labels);

        JsonArray? target = null;
        foreach (var r in rows)
        {
            if (r is not JsonArray row) continue;
            var (rnum, rname) = RowKey(row, cols);
            if (num.Length > 0 && rnum == num) { target = row; break; }
            if (num.Length == 0 && name.Length > 0 && rname == name) { target = row; break; }
        }
        string result;
        if (target == null)
        {
            var nr = new JsonArray();
            for (int i = 0, n = Math.Max(labels.Count, Math.Max(cols.num, Math.Max(cols.name, cols.content)) + 1); i < n; i++) nr.Add("");
            nr[cols.num] = num; nr[cols.name] = name; nr[cols.content] = text;
            rows.Add(nr); result = "insert";
        }
        else
        {
            while (target.Count <= cols.content) target.Add("");
            string cur = Str(target[cols.content]).Trim();
            target[cols.content] = cur.Length > 0 ? $"{cur}\n{text}" : text; result = "append";
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
        var outp = new List<(string, string, string)>();
        var seen = new HashSet<(string, string, string)>();
        foreach (var f in RosterFiles(dir, area))
            foreach (var (klass, entry, rows) in Sheets(f))
            {
                var cols = KeyCols(Labels(entry));
                foreach (var r in rows)
                {
                    if (r is not JsonArray row) continue;
                    var (num, name) = RowKey(row, cols);
                    if (num.Length == 0 && name.Length == 0) continue;
                    var key = (klass, num, name);
                    if (seen.Add(key)) outp.Add(key);
                }
            }
        return outp;
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
