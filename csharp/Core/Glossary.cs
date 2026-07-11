namespace Saenggibu;

/// <summary>
/// app/glossary.py 이식 중 결정론 변환(정규화·words). kiwi 등록은 2단계.
/// phrases()는 Python이 set을 순회해 동률 순서가 비결정적이라 exact 골든 제외.
/// </summary>
public static class Glossary
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
