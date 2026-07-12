namespace Saenggibu;

/// <summary>
/// app/glossary.py 이식 중 결정론 변환(정규화·words). kiwi 등록은 2단계.
/// phrases()는 Python이 set을 순회해 동률 순서가 비결정적이라 exact 골든 제외.
/// </summary>
public class Glossary
{
    /// <summary>공백 정규화(연속공백→하나) + 빈 항목 제거 → 용어(구) 집합.</summary>
    public static HashSet<string> NormalizeTerms(IEnumerable<string> raw)
    {
        var outp = new HashSet<string>();
        foreach (var s in raw)
        {
            if (string.IsNullOrWhiteSpace(s)) continue;
            outp.Add(string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
        }
        return outp;
    }

    // 파일 저장(app/glossary.py 의 user_terms.json). 인스턴스로 dataDir 지정.
    private readonly string _path;
    public Glossary(string dataDir) => _path = System.IO.Path.Combine(dataDir, "user_terms.json");

    public HashSet<string> AllTerms()
    {
        try
        {
            var raw = System.Text.Json.JsonSerializer.Deserialize<List<string>>(System.IO.File.ReadAllText(_path));
            return NormalizeTerms(raw ?? new());
        }
        catch (Exception e) when (e is System.IO.IOException or System.Text.Json.JsonException) { return new(); }
    }

    public bool Add(string term)
    {
        term = string.Join(" ", (term ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (term.Length < 2) return false;
        var t = AllTerms();
        if (!t.Add(term)) return false;
        Save(t); return true;
    }

    public bool Remove(string term)
    {
        term = string.Join(" ", (term ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var t = AllTerms();
        if (!t.Remove(term)) return false;
        Save(t); return true;
    }

    private void Save(HashSet<string> t)
    {
        var opts = new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        System.IO.File.WriteAllText(_path, System.Text.Json.JsonSerializer.Serialize(t.OrderBy(x => x, StringComparer.Ordinal), opts));
    }

    /// <summary>모든 구를 이루는 개별 단어(2자 이상).</summary>
    public static HashSet<string> Words(IEnumerable<string> terms)
    {
        var outp = new HashSet<string>();
        foreach (var t in terms)
            foreach (var w in t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                if (w.Length >= 2) outp.Add(w);
        return outp;
    }
}
