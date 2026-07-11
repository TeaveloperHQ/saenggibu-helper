using System.Text;
using System.Text.Json;
using Saenggibu;

// Tier A 골든셋 회귀 러너 — golden.json(파이썬 진리)과 C# 구현을 exact-match 비교.
// 사용: dotnet run --project Cli -- <golden.json 경로(생략 시 ../golden/golden.json)>

Console.OutputEncoding = Encoding.UTF8;

// 서브커맨드: infer(Tier B 추론 스모크). 기본은 Tier A 골든 회귀.
if (args.Length > 0 && args[0] == "infer")
    return await Cli.Infer.RunAsync(args);

string goldenPath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "golden", "golden.json");
goldenPath = Path.GetFullPath(goldenPath);

if (!File.Exists(goldenPath))
{
    Console.Error.WriteLine($"golden.json 없음: {goldenPath}");
    return 2;
}

using var doc = JsonDocument.Parse(File.ReadAllText(goldenPath));
var root = doc.RootElement;

int pass = 0, fail = 0;
var failures = new List<string>();

void Check(string suite, int idx, string expected, string actual, string? inputDesc = null)
{
    if (expected == actual) { pass++; return; }
    fail++;
    if (failures.Count < 25)
        failures.Add($"[{suite} #{idx}] {inputDesc}\n  기대: {Trunc(expected)}\n  실제: {Trunc(actual)}");
}

static string Trunc(string s) => s.Length <= 200 ? s : s[..200] + "…";
static string J(JsonElement e) => e.GetString() ?? "";
static List<string> JArr(JsonElement e) => e.EnumerateArray().Select(x => x.GetString() ?? "").ToList();

IEnumerable<(JsonElement, int)> Iter(string key)
{
    if (!root.TryGetProperty(key, out var arr)) yield break;
    int i = 0;
    foreach (var el in arr.EnumerateArray()) yield return (el, i++);
}

// ── tokenize ──────────────────────────────────────────────────────────
foreach (var (c, i) in Iter("tokenize"))
    Check("tokenize", i, string.Join("", JArr(c.GetProperty("out"))),
          string.Join("", Tokenizer.Tokenize(J(c.GetProperty("in")))), J(c.GetProperty("in")));

// ── nominalize_sentence ────────────────────────────────────────────────
foreach (var (c, i) in Iter("nominalize_sentence"))
    Check("nominalize_sentence", i, J(c.GetProperty("out")),
          Postprocess.NominalizeSentence(J(c.GetProperty("in"))), J(c.GetProperty("in")));

// ── to_nominal ─────────────────────────────────────────────────────────
foreach (var (c, i) in Iter("to_nominal"))
    Check("to_nominal", i, J(c.GetProperty("out")),
          Postprocess.ToNominalEndings(J(c.GetProperty("in"))), Trunc(J(c.GetProperty("in"))));

// ── split_sentences ────────────────────────────────────────────────────
foreach (var (c, i) in Iter("split_sentences"))
    Check("split_sentences", i,
          string.Join("", JArr(c.GetProperty("out"))),
          string.Join("", Variation.SplitSentences(J(c.GetProperty("in")))),
          J(c.GetProperty("in")));

// ── classify ───────────────────────────────────────────────────────────
foreach (var (c, i) in Iter("classify"))
{
    var exp = c.GetProperty("out");
    var got = Patterns.Classify(J(c.GetProperty("in")));
    string e = $"{J(exp.GetProperty("comp"))}|{J(exp.GetProperty("end"))}|{J(exp.GetProperty("order"))}|{J(exp.GetProperty("conn"))}";
    string a = $"{got["comp"]}|{got["end"]}|{got["order"]}|{got["conn"]}";
    Check("classify", i, e, a, J(c.GetProperty("in")));
}

// ── bm25_rank ──────────────────────────────────────────────────────────
foreach (var (c, i) in Iter("bm25_rank"))
{
    var docs = c.GetProperty("docs").EnumerateArray()
        .Select(d => new Bm25.Doc(J(d.GetProperty("keywords")), J(d.GetProperty("subject"))))
        .ToList();
    var got = Bm25.Rank(J(c.GetProperty("query")), J(c.GetProperty("subject")), docs,
                        c.GetProperty("k").GetInt32());
    string e = string.Join(",", c.GetProperty("out").EnumerateArray().Select(x => x.GetInt32()));
    Check("bm25_rank", i, e, string.Join(",", got), J(c.GetProperty("query")));
}

// ── system_prompt ──────────────────────────────────────────────────────
foreach (var (c, i) in Iter("system_prompt"))
{
    var area = Prompts.ByKey(J(c.GetProperty("area")))!;
    Check("system_prompt", i, J(c.GetProperty("out")), area.SystemPrompt(), J(c.GetProperty("area")));
}

// ── build_user_prompt ──────────────────────────────────────────────────
foreach (var (c, i) in Iter("build_user_prompt"))
{
    var area = Prompts.ByKey(J(c.GetProperty("area")))!;
    var got = Prompts.BuildUserPrompt(area, J(c.GetProperty("subject")), J(c.GetProperty("keywords")),
        J(c.GetProperty("tone")), J(c.GetProperty("length_hint")), c.GetProperty("n").GetInt32());
    Check("build_user_prompt", i, J(c.GetProperty("out")), got,
          $"{J(c.GetProperty("area"))} n={c.GetProperty("n").GetInt32()}");
}

// ── paraphrase(순수 헬퍼) ──────────────────────────────────────────────
foreach (var (c, i) in Iter("fix_spacing"))
    Check("fix_spacing", i, J(c.GetProperty("out")),
          Paraphrase.FixSpacing(J(c.GetProperty("in"))), J(c.GetProperty("in")));

foreach (var (c, i) in Iter("clean_line"))
    Check("clean_line", i, J(c.GetProperty("out")),
          Paraphrase.CleanLine(J(c.GetProperty("in"))), J(c.GetProperty("in")));

foreach (var (c, i) in Iter("is_eval_sent"))
    Check("is_eval_sent", i, c.GetProperty("out").GetBoolean().ToString(),
          Paraphrase.IsEvalSent(J(c.GetProperty("in"))).ToString(), J(c.GetProperty("in")));

foreach (var (c, i) in Iter("too_similar"))
    Check("too_similar", i, c.GetProperty("out").GetBoolean().ToString(),
          Paraphrase.TooSimilar(J(c.GetProperty("a")), J(c.GetProperty("b"))).ToString(),
          $"{J(c.GetProperty("a"))} vs {J(c.GetProperty("b"))}");

foreach (var (c, i) in Iter("bigrams"))
    Check("bigrams", i, string.Join(",", JArr(c.GetProperty("out"))),
          string.Join(",", Paraphrase.Bigrams(J(c.GetProperty("in"))).OrderBy(x => x, StringComparer.Ordinal)),
          J(c.GetProperty("in")));

// ── retrieve(SQLite end-to-end) ────────────────────────────────────────
string retrPath = Path.Combine(Path.GetDirectoryName(goldenPath)!, "golden_retrieve.json");
if (File.Exists(retrPath))
{
    using var rdoc = JsonDocument.Parse(File.ReadAllText(retrPath));
    var rroot = rdoc.RootElement;
    string dbPath = Path.Combine(Path.GetDirectoryName(retrPath)!, rroot.GetProperty("db").GetString()!);
    using var store = new MemoryStore(dbPath);

    int ri = 0;
    foreach (var c in rroot.GetProperty("retrieve").EnumerateArray())
    {
        var got = store.Retrieve(J(c.GetProperty("area")), J(c.GetProperty("query")),
                                 c.GetProperty("k").GetInt32(), J(c.GetProperty("subject")));
        Check("retrieve", ri++, string.Join("|", JArr(c.GetProperty("out"))),
              string.Join("|", got.Select(e => e.OutputText)), J(c.GetProperty("query")));
    }
    int si = 0;
    foreach (var c in rroot.GetProperty("retrieve_seed").EnumerateArray())
    {
        var got = store.RetrieveSeed(J(c.GetProperty("area")), J(c.GetProperty("query")),
                                     c.GetProperty("k").GetInt32(), J(c.GetProperty("subject")));
        Check("retrieve_seed", si++, string.Join("|", JArr(c.GetProperty("out"))),
              string.Join("|", got.Select(e => e.OutputText)), J(c.GetProperty("query")));
    }

    static string SerMsgs(IEnumerable<Engine.Message> ms) =>
        string.Join("", ms.Select(m => m.Role + "" + m.Content));
    int mi = 0;
    foreach (var c in rroot.GetProperty("build_messages").EnumerateArray())
    {
        var area = Prompts.ByKey(J(c.GetProperty("area")))!;
        var got = Engine.BuildMessages(area, store, J(c.GetProperty("subject")),
            J(c.GetProperty("keywords")), J(c.GetProperty("tone")),
            J(c.GetProperty("length_hint")), c.GetProperty("n").GetInt32());
        string exp = string.Join("", c.GetProperty("out").EnumerateArray()
            .Select(m => J(m.GetProperty("role")) + "" + J(m.GetProperty("content"))));
        Check("build_messages", mi++, exp, SerMsgs(got),
              $"{J(c.GetProperty("area"))} n={c.GetProperty("n").GetInt32()}");
    }
}

// ── 결과 ───────────────────────────────────────────────────────────────
Console.WriteLine($"\n=== Tier A 골든 회귀: {pass} PASS / {fail} FAIL ===");
foreach (var f in failures) Console.WriteLine("\n" + f);
return fail == 0 ? 0 : 1;
