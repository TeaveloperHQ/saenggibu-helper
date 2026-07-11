namespace Saenggibu;

/// <summary>
/// app/memory_store.py 의 _bm25_scores / _boost_subject / retrieve 랭킹 이식.
/// 표준 BM25(k1=1.5, b=0.75), 같은 과목 +1000 부스트, 점수 내림차순 안정 정렬.
/// </summary>
public static class Bm25
{
    public sealed record Doc(string Keywords, string Subject);

    public static double[] Scores(List<string> queryTokens, List<List<string>> corpus,
                                  double k1 = 1.5, double b = 0.75)
    {
        int n = corpus.Count;
        if (n == 0) return Array.Empty<double>();

        var docLen = new int[n];
        double totalLen = 0;
        for (int i = 0; i < n; i++) { docLen[i] = corpus[i].Count; totalLen += docLen[i]; }
        double avgdl = n > 0 ? totalLen / n : 0.0;

        // document frequency
        var df = new Dictionary<string, int>();
        foreach (var doc in corpus)
            foreach (var term in new HashSet<string>(doc))
                df[term] = df.TryGetValue(term, out var c) ? c + 1 : 1;

        // term frequency per doc
        var tf = new Dictionary<string, int>[n];
        for (int i = 0; i < n; i++)
        {
            tf[i] = new Dictionary<string, int>();
            foreach (var term in corpus[i])
                tf[i][term] = tf[i].TryGetValue(term, out var c) ? c + 1 : 1;
        }

        var scores = new double[n];
        var qTerms = new HashSet<string>(queryTokens);
        foreach (var term in qTerms)
        {
            if (!df.TryGetValue(term, out var dft)) continue;
            double idf = Math.Log(1 + (n - dft + 0.5) / (dft + 0.5));
            for (int i = 0; i < n; i++)
            {
                int f = tf[i].TryGetValue(term, out var ff) ? ff : 0;
                if (f == 0) continue;
                double denom = f + k1 * (1 - b + b * (avgdl != 0 ? docLen[i] / avgdl : 0));
                scores[i] += idf * (f * (k1 + 1)) / denom;
            }
        }
        return scores;
    }

    private static string NormSubject(string? s) => (s ?? "").Trim().ToLowerInvariant();

    public static double[] BoostSubject(double[] scores, IReadOnlyList<Doc> docs, string subject,
                                        double bonus = 1000.0)
    {
        var subj = NormSubject(subject);
        if (subj.Length == 0) return scores;
        var outp = new double[scores.Length];
        for (int i = 0; i < scores.Length; i++)
            outp[i] = NormSubject(docs[i].Subject) == subj ? scores[i] + bonus : scores[i];
        return outp;
    }

    /// <summary>retrieve() 랭킹 재현 — 정렬된 문서 인덱스(상위 k). s>0 우선, 없으면 상위 k.</summary>
    public static List<int> Rank(string query, string subject, IReadOnlyList<Doc> docs, int k)
    {
        int n = docs.Count;
        if (n == 0) return new List<int>();
        var corpus = new List<List<string>>(n);
        for (int i = 0; i < n; i++)
            corpus.Add(Tokenizer.Tokenize($"{docs[i].Keywords} {docs[i].Subject}"));
        var scores = Scores(Tokenizer.Tokenize(query), corpus);
        scores = BoostSubject(scores, docs, subject);

        // 점수 내림차순 안정 정렬(원 인덱스 순서 유지) — Python sorted(reverse=True)와 동일
        var order = Enumerable.Range(0, n)
            .OrderByDescending(i => scores[i])
            .ToList();
        var top = order.Take(k).ToList();
        var positive = top.Where(i => scores[i] > 0).ToList();
        return positive.Count > 0 ? positive : top;
    }
}
