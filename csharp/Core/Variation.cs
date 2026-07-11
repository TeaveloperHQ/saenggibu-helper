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

    private static readonly Regex Ws = new(@"\s+", RegexOptions.Compiled);
    private static string Norm(string s) => Ws.Replace(s, "");

    private static string EnsurePeriod(string s)
    {
        s = s.Trim();
        return (s.EndsWith(".") || s.EndsWith("!") || s.EndsWith("?")) ? s : s + ".";
    }

    private static long Factorial(int k)
    {
        long f = 1;
        for (int i = 2; i <= k; i++) f *= i;
        return f;
    }

    /// <summary>app/variation.py combine_variants — 어순 재배열+표현 선택 조합(PyRandom(42) 결정론).</summary>
    public static List<string> CombineVariants(List<List<string>> groups, int n)
    {
        groups = groups.Select(g => g.Where(p => p.Trim().Length > 0).Select(EnsurePeriod).ToList())
                       .Where(g => g.Count > 0).ToList();
        int k = groups.Count;
        if (k == 0) return new List<string>();

        var outp = new List<string>();
        var seen = new HashSet<string>();

        if (k == 1)
        {
            foreach (var bas in DedupPreserveOrder(groups[0]))
                foreach (var v in SynonymVariants(bas))
                {
                    if (seen.Add(Norm(v))) { outp.Add(v); if (outp.Count >= n) return outp; }
                }
            return outp;
        }

        long space = Factorial(k);
        foreach (var g in groups) space *= g.Count;
        long target = Math.Min(n, space);

        var rng = new PyRandom(42);
        var idxs = Enumerable.Range(0, k).ToList();
        int attempts = 0;
        long maxAttempts = target * 80 + 500;
        while (outp.Count < target && attempts < maxAttempts)
        {
            attempts++;
            var perm = new List<int>(idxs);
            rng.Shuffle(perm);
            var parts = new List<string>(k);
            foreach (var i in perm) parts.Add(rng.Choice(groups[i]));  // perm 순서로 choice 소비
            string text = string.Join(" ", parts);
            if (seen.Add(Norm(text))) outp.Add(text);
        }

        if (outp.Count < n)
            foreach (var t in outp.ToList())
                foreach (var v in SynonymVariants(t))
                {
                    if (seen.Add(Norm(v))) { outp.Add(v); if (outp.Count >= n) return outp; }
                }
        return outp.Count > n ? outp.Take(n).ToList() : outp;
    }
}
