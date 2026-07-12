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

    private readonly string _dbPath;
    public MemoryStore(string dbPath)
    {
        _dbPath = dbPath;
        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        InitSchema();
    }

    private void InitSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS examples(id INTEGER PRIMARY KEY AUTOINCREMENT, area TEXT NOT NULL,
                subject TEXT DEFAULT '', keywords TEXT NOT NULL, output_text TEXT NOT NULL,
                rating INTEGER DEFAULT 1, created_at REAL NOT NULL);
            CREATE INDEX IF NOT EXISTS idx_examples_area ON examples(area);
            CREATE TABLE IF NOT EXISTS seed_examples(id INTEGER PRIMARY KEY AUTOINCREMENT, area TEXT NOT NULL,
                subject TEXT DEFAULT '', keywords TEXT NOT NULL, output_text TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS idx_seed_area ON seed_examples(area);
            CREATE TABLE IF NOT EXISTS meta(k TEXT PRIMARY KEY, v TEXT);";
        cmd.ExecuteNonQuery();
    }

    /// <summary>app/memory_store.py add_example — 채택 예시 저장(학습).</summary>
    public long AddExample(string area, string subject, string keywords, string outputText, int rating = 1)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT INTO examples(area, subject, keywords, output_text, rating, created_at) " +
                          "VALUES($a,$s,$k,$o,$r,$t); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$a", area);
        cmd.Parameters.AddWithValue("$s", subject ?? "");
        cmd.Parameters.AddWithValue("$k", keywords ?? "");
        cmd.Parameters.AddWithValue("$o", outputText);
        cmd.Parameters.AddWithValue("$r", rating);
        cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    public int Count(string? area = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = area == null ? "SELECT COUNT(*) FROM examples" : "SELECT COUNT(*) FROM examples WHERE area=$a";
        if (area != null) cmd.Parameters.AddWithValue("$a", area);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public int SeedCount()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM seed_examples";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>씨드 코퍼스(JSONL) 적재(해시로 idempotent).</summary>
    public int LoadSeedCorpus(string path)
    {
        if (!File.Exists(path)) return SeedCount();
        string digest;
        using (var sha = System.Security.Cryptography.SHA256.Create())
            digest = Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(path)));
        using (var chk = _conn.CreateCommand())
        {
            chk.CommandText = "SELECT v FROM meta WHERE k='seed_hash'";
            if (Convert.ToString(chk.ExecuteScalar()) == digest && SeedCount() > 0) return SeedCount();
        }
        using var tx = _conn.BeginTransaction();
        using (var del = _conn.CreateCommand()) { del.CommandText = "DELETE FROM seed_examples"; del.ExecuteNonQuery(); }
        int nrows = 0;
        foreach (var line in File.ReadLines(path))
        {
            var s = line.Trim();
            if (s.Length == 0 || s.StartsWith("#")) continue;
            try
            {
                using var d = System.Text.Json.JsonDocument.Parse(s);
                var o = d.RootElement;
                if (!o.TryGetProperty("area", out var ar) || !o.TryGetProperty("output", out var ou)) continue;
                using var ins = _conn.CreateCommand();
                ins.CommandText = "INSERT INTO seed_examples(area,subject,keywords,output_text) VALUES($a,$s,$k,$o)";
                ins.Parameters.AddWithValue("$a", ar.GetString() ?? "");
                ins.Parameters.AddWithValue("$s", o.TryGetProperty("subject", out var sj) ? sj.GetString() ?? "" : "");
                ins.Parameters.AddWithValue("$k", o.TryGetProperty("keywords", out var kw) ? kw.GetString() ?? "" : "");
                ins.Parameters.AddWithValue("$o", ou.GetString() ?? "");
                ins.ExecuteNonQuery(); nrows++;
            }
            catch { }
        }
        using (var mm = _conn.CreateCommand())
        {
            mm.CommandText = "INSERT INTO meta(k,v) VALUES('seed_hash',$v) ON CONFLICT(k) DO UPDATE SET v=excluded.v";
            mm.Parameters.AddWithValue("$v", digest); mm.ExecuteNonQuery();
        }
        tx.Commit();
        return nrows;
    }

    /// <summary>학습 데이터 백업(파일 복사).</summary>
    public void Backup(string destPath) { using var chk = _conn.CreateCommand(); chk.CommandText = "PRAGMA wal_checkpoint(FULL)"; chk.ExecuteNonQuery(); File.Copy(_dbPath, destPath, true); }

    /// <summary>백업의 examples 병합(중복 제외). 추가된 수 반환.</summary>
    public int ImportMerge(string srcPath)
    {
        using var src = new SqliteConnection($"Data Source={srcPath};Mode=ReadOnly");
        src.Open();
        var existing = new HashSet<(string, string)>();
        using (var ex = _conn.CreateCommand()) { ex.CommandText = "SELECT area, output_text FROM examples"; using var r = ex.ExecuteReader(); while (r.Read()) existing.Add((r.GetString(0), r.GetString(1))); }
        int added = 0;
        using var q = src.CreateCommand();
        q.CommandText = "SELECT area, subject, keywords, output_text, rating, created_at FROM examples";
        using var rd = q.ExecuteReader();
        while (rd.Read())
        {
            var key = (rd.GetString(0), rd.GetString(3));
            if (!existing.Add(key)) continue;
            using var ins = _conn.CreateCommand();
            ins.CommandText = "INSERT INTO examples(area,subject,keywords,output_text,rating,created_at) VALUES($a,$s,$k,$o,$r,$t)";
            ins.Parameters.AddWithValue("$a", rd.GetString(0)); ins.Parameters.AddWithValue("$s", rd.GetString(1));
            ins.Parameters.AddWithValue("$k", rd.GetString(2)); ins.Parameters.AddWithValue("$o", rd.GetString(3));
            ins.Parameters.AddWithValue("$r", rd.GetInt32(4)); ins.Parameters.AddWithValue("$t", rd.GetDouble(5));
            ins.ExecuteNonQuery(); added++;
        }
        return added;
    }

    public List<Example> RowsForArea(string area)
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
