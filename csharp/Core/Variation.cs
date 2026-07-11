using System.Text.RegularExpressions;

namespace Saenggibu;

/// <summary>
/// app/variation.py 이식 중 결정론적 부분(split_sentences, synonym_variants).
/// combine/expand 는 random.Random(42) 의존이라 별도(Python 호환 MT 필요) — 후속.
/// </summary>
public static class Variation
{
    public static readonly List<List<string>> Synonyms = new()
    {
        new() { "보임", "드러냄", "나타냄" },
        new() { "노력함", "힘씀" },
        new() { "적극적으로", "능동적으로", "자발적으로" },
        new() { "성실히", "성실하게", "꾸준히" },
        new() { "꼼꼼히", "세심하게" },
        new() { "끝까지", "빠짐없이" },
        new() { "태도", "자세" },
        new() { "능력", "역량" },
        new() { "친구들", "또래들" },
        new() { "수행함", "해냄", "완수함" },
        new() { "참여함", "임함" },
    };

    private const int SynCap = 8;

    private static readonly Regex SentRe = new(@"[^.]+\.", RegexOptions.Compiled);

    public static List<string> SplitSentences(string text)
    {
        var replaced = text.Replace("\n", " ");
        var outp = SentRe.Matches(replaced)
            .Select(m => m.Value.Trim())
            .Where(s => s.Length > 0)
            .ToList();
        if (outp.Count == 0 && text.Trim().Length > 0)
            outp = new List<string> { text.Trim() };
        return outp;
    }

    public static List<string> SynonymVariants(string text, int cap = SynCap)
    {
        var results = new List<string> { text };
        foreach (var group in Synonyms)
        {
            string? present = group.FirstOrDefault(w => text.Contains(w, StringComparison.Ordinal));
            if (present is null) continue;
            var expanded = new List<string>();
            foreach (var r in results)
                foreach (var w in group)
                    expanded.Add(r.Replace(present, w));
            // 중복 제거(순서 보존) + 상한
            results = DedupPreserveOrder(expanded).Take(cap).ToList();
        }
        return results.Take(cap).ToList();
    }

    private static List<string> DedupPreserveOrder(IEnumerable<string> items)
    {
        var seen = new HashSet<string>();
        var outp = new List<string>();
        foreach (var s in items)
            if (seen.Add(s)) outp.Add(s);
        return outp;
    }
}
