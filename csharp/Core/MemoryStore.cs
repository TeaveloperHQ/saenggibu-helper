using Microsoft.Data.Sqlite;

namespace Saenggibu;

/// <summary>
/// app/memory_store.py 의 읽기·검색(retrieve) 경로 이식 — 파리티 핵심 경로.
/// 스키마·쓰기 전체는 후속. 지금은 examples/seed_examples 조회 + BM25 랭킹 재현.
/// </summary>
public sealed class MemoryStore : IDisposable
{
    public sealed record Example(int Id, string Area, string Subject, string Keywords,
                                 string OutputText, int Rating, double CreatedAt);

    private readonly SqliteConnection _conn;

    public MemoryStore(string dbPath)
    {
        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
    }

    private List<Example> RowsForArea(string area)
    {
        // Python: SELECT * FROM examples WHERE area=? AND rating>=1 (rowid 순)
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, area, subject, keywords, output_text, rating, created_at " +
                          "FROM examples WHERE area=$a AND rating>=1";
        cmd.Parameters.AddWithValue("$a", area);
        return ReadExamples(cmd);
    }

    private List<Example> SeedRowsForArea(string area)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, area, subject, keywords, output_text FROM seed_examples WHERE area=$a";
        cmd.Parameters.AddWithValue("$a", area);
        var outp = new List<Example>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            outp.Add(new Example(-1, area, r.GetString(2), r.GetString(3), r.GetString(4), 1, 0.0));
        return outp;
    }

    private static List<Example> ReadExamples(SqliteCommand cmd)
    {
        var outp = new List<Example>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            outp.Add(new Example(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3),
                                 r.GetString(4), r.GetInt32(5), r.GetDouble(6)));
        return outp;
    }

    private static List<Example> RankPick(List<Example> docs, string query, string subject, int k,
                                          bool requirePositive)
    {
        if (docs.Count == 0) return new List<Example>();
        var bdocs = docs.Select(d => new Bm25.Doc(d.Keywords, d.Subject)).ToList();
        // Bm25.Rank 은 s>0 우선/폴백을 이미 수행 → retrieve(examples)와 동일
        // retrieve_seed 는 s>0 필터 없이 상위 k → requirePositive=false 경로
        int n = docs.Count;
        var corpus = docs.Select(d => Tokenizer.Tokenize($"{d.Keywords} {d.Subject}")).ToList();
        var scores = Bm25.Scores(Tokenizer.Tokenize(query), corpus);
        scores = Bm25.BoostSubject(scores, bdocs, subject);
        var order = Enumerable.Range(0, n).OrderByDescending(i => scores[i]).ToList();
        var top = order.Take(k).ToList();
        if (!requirePositive) return top.Select(i => docs[i]).ToList();
        var positive = top.Where(i => scores[i] > 0).ToList();
        var pick = positive.Count > 0 ? positive : top;
        return pick.Select(i => docs[i]).ToList();
    }

    /// <summary>app/memory_store.py retrieve — examples(rating>=1)에서 상위 k.</summary>
    public List<Example> Retrieve(string area, string query, int k, string subject = "")
        => RankPick(RowsForArea(area), query, subject, k, requirePositive: true);

    /// <summary>app/memory_store.py retrieve_seed — seed_examples에서 상위 k(양수 필터 없음).</summary>
    public List<Example> RetrieveSeed(string area, string query, int k, string subject = "")
        => RankPick(SeedRowsForArea(area), query, subject, k, requirePositive: false);

    public void Dispose() => _conn.Dispose();
}
