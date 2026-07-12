using System.Text;
using System.Text.Json;
using Saenggibu;

// Tier A 골든셋 회귀 러너 — golden.json(파이썬 진리)과 C# 구현을 exact-match 비교.
// 사용: dotnet run --project Cli -- <golden.json 경로(생략 시 ../golden/golden.json)>

Console.OutputEncoding = Encoding.UTF8;

// 서브커맨드: infer(Tier B 추론 스모크). 기본은 Tier A 골든 회귀.
if (args.Length > 0 && args[0] == "infer")
    return await Cli.Infer.RunAsync(args);

// 서브커맨드: kiwi <modelPath> "<문장>" — Kiwi C-API P/Invoke 형태소 분석 스모크
if (args.Length > 0 && args[0] == "kiwi")
{
    Console.OutputEncoding = Encoding.UTF8;
    Console.WriteLine($"Kiwi {KiwiNative.Version()}");
    using var kiwi = new KiwiNative(args[1]);
    string sent = args.Length > 2 ? args[2] : "산과 염기 반응을 지시약으로 확인하는 실험을 설계함";
    foreach (var (form, tag) in kiwi.Tokenize(sent))
        Console.WriteLine($"{form}\t{tag}");
    Console.WriteLine("--- join ---");
    Console.WriteLine(kiwi.Join(new[] { ("수행", "NNG"), ("하", "XSV"), ("며", "EC") }));
    Console.WriteLine(kiwi.Join(new[] { ("책임", "NNG"), ("감", "XSN"), ("을", "JKO"), ("보이", "VV"), ("ᆷ", "ETN") }));
    return 0;
}

// 서브커맨드: ptest <modelPath> <sentence> — _alternatives/_reorder 파리티 출력
if (args.Length > 0 && args[0] == "ptest")
{
    Console.OutputEncoding = Encoding.UTF8;
    using var kiwi = new KiwiNative(args[1]);
    var morphs = kiwi.Tokenize(args[2]).ToList();
    // alternatives: 후보>1인 위치만 "form:a|b|c"
    var alts = Paraphrase.Alternatives(morphs);
    var altStr = Enumerable.Range(0, morphs.Count).Where(i => alts[i].Count > 1)
        .Select(i => $"{morphs[i].form}:{string.Join("|", alts[i])}");
    Console.WriteLine("ALT " + string.Join(" ", altStr));
    // reorder(PyRandom(42)) → join
    var reordered = Paraphrase.Reorder(morphs, new PyRandom(42));
    Console.WriteLine("REORDER " + kiwi.Join(reordered));
    return 0;
}

// 서브커맨드: bench <modelPath> — 결정적 핫패스 처리량 측정(C# vs Python)
if (args.Length > 0 && args[0] == "bench")
{
    Console.OutputEncoding = Encoding.UTF8;
    using var kiwi = new KiwiNative(args[1]);
    var sents = new[]
    {
        "맡은 역할을 성실히 수행하며 책임감을 보임", "실험을 설계하고 결과를 분석함",
        "친구를 도와 문제를 해결함", "논리적 사고력을 보임",
        "자료를 정리하고 방법을 설명하며 의견을 제시함", "탐구하는 태도를 지님",
    };
    var sw = new System.Diagnostics.Stopwatch();

    // 1) BM25 tokenize (순수 관리형)
    int nTok = 200_000; sw.Restart();
    long acc = 0;
    for (int i = 0; i < nTok; i++) acc += Tokenizer.Tokenize(sents[i % sents.Length]).Count;
    sw.Stop();
    Console.WriteLine($"BM25.tokenize\t{(nTok / sw.Elapsed.TotalSeconds).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} ops/s");

    // 2) kiwi tokenize (네이티브 P/Invoke)
    int nKiwi = 5_000; sw.Restart();
    for (int i = 0; i < nKiwi; i++) kiwi.Tokenize(sents[i % sents.Length]);
    sw.Stop();
    Console.WriteLine($"kiwi.tokenize\t{(nKiwi / sw.Elapsed.TotalSeconds).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} ops/s");

    // 3) _sentence_variants (변형 원자, kiwi+rng+join)
    int nSv = 2_000; sw.Restart();
    for (int i = 0; i < nSv; i++)
        Paraphrase.SentenceVariants(sents[i % sents.Length], 6, new PyRandom(42), true, kiwi);
    sw.Stop();
    Console.WriteLine($"sentence_variants\t{(nSv / sw.Elapsed.TotalSeconds).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} ops/s");
    return 0;
}

// 서브커맨드: validtest <model> <original> <candidate> — _valid 파리티
if (args.Length > 0 && args[0] == "validtest")
{
    Console.OutputEncoding = Encoding.UTF8;
    using var kiwi = new KiwiNative(args[1]);
    var (allk, crit) = Paraphrase.Nouns(args[2], kiwi, Array.Empty<string>());
    Console.WriteLine(Paraphrase.Valid(args[3], allk, crit, args[2], kiwi, Array.Empty<string>()) ? "True" : "False");
    return 0;
}
// 서브커맨드: josatest <model> <text> — _fix_josa_ro 파리티
if (args.Length > 0 && args[0] == "josatest")
{
    Console.OutputEncoding = Encoding.UTF8;
    using var kiwi = new KiwiNative(args[1]);
    Console.WriteLine(Paraphrase.FixJosaRo(args[2], kiwi));
    return 0;
}

// 서브커맨드: plantest <n> <seed> — patterns.plan/instruction 파리티
if (args.Length > 0 && args[0] == "plantest")
{
    Console.OutputEncoding = Encoding.UTF8;
    var plan = Patterns.Plan(int.Parse(args[1]), Patterns.DefaultProfile, new PyRandom(long.Parse(args[2])));
    foreach (var t in plan)
        Console.WriteLine($"{t["comp"]}|{t["end"]}|{t["order"]}|{t["conn"]} :: {Patterns.Instruction(t)}");
    return 0;
}

// 서브커맨드: gguftest <path> — LooksLikeGguf 파리티
if (args.Length > 0 && args[0] == "gguftest")
{
    Console.WriteLine(Downloader.LooksLikeGguf(args[1]) ? "True" : "False");
    return 0;
}

// 서브커맨드: xlsxtest <path> — parse_xlsx 파리티
if (args.Length > 0 && args[0] == "xlsxtest")
{
    Console.OutputEncoding = Encoding.UTF8;
    foreach (var r in Importer.ParseXlsx(args[1])) Console.WriteLine(r);
    return 0;
}

// 서브커맨드: mtest <modelPath> <sent> <n> [subject] — _mechanical 파리티
if (args.Length > 0 && args[0] == "mtest")
{
    Console.OutputEncoding = Encoding.UTF8;
    using var kiwi = new KiwiNative(args[1]);
    var res = Paraphrase.Mechanical(args[2], int.Parse(args[3]), kiwi, 42,
        args.Length > 4 ? args[4] : "");
    foreach (var v in res) Console.WriteLine(v);
    return 0;
}

// 서브커맨드: rostertest <dir> <area> — roster_data 파리티
if (args.Length > 0 && args[0] == "rostertest")
{
    Console.OutputEncoding = Encoding.UTF8;
    foreach (var kv in RosterData.ClassesAndStudents(args[1], args.Length > 2 ? args[2] : null))
        Console.WriteLine($"CS {kv.Key}: {string.Join(", ", kv.Value)}");
    if (args.Length > 2)
        foreach (var (klass, num, name) in RosterData.RosterRecords(args[1], args[2]))
            Console.WriteLine($"REC {klass}|{num}|{name}");
    return 0;
}

// 서브커맨드: rtest <modelPath> <sent> <n> — _recombine_paraphrase 파리티
if (args.Length > 0 && args[0] == "rtest")
{
    Console.OutputEncoding = Encoding.UTF8;
    using var kiwi = new KiwiNative(args[1]);
    var res = Paraphrase.RecombineParaphrase(args[2], int.Parse(args[3]), kiwi,
        Array.Empty<string>(), Array.Empty<string>());
    foreach (var v in res) Console.WriteLine(v);
    return 0;
}

// 서브커맨드: svtest <modelPath> <sent> <k> <adv0|1> — _sentence_variants 파리티
if (args.Length > 0 && args[0] == "svtest")
{
    Console.OutputEncoding = Encoding.UTF8;
    using var kiwi = new KiwiNative(args[1]);
    var vars = Paraphrase.SentenceVariants(args[2], int.Parse(args[3]),
        new PyRandom(42), args[4] == "1", kiwi);
    foreach (var v in vars) Console.WriteLine(v);
    return 0;
}

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

// ── importer / glossary / roster (순수) ────────────────────────────────
foreach (var (c, i) in Iter("extract_keywords"))
    Check("extract_keywords", i, J(c.GetProperty("out")),
          Importer.ExtractKeywords(J(c.GetProperty("in"))), J(c.GetProperty("in")));

foreach (var (c, i) in Iter("parse_records"))
    Check("parse_records", i, string.Join("¶", JArr(c.GetProperty("out"))),
          string.Join("¶", Importer.ParseRecords(J(c.GetProperty("in")), J(c.GetProperty("mode")))),
          $"{J(c.GetProperty("mode"))}");

foreach (var (c, i) in Iter("glossary_words"))
    Check("glossary_words", i, string.Join(",", JArr(c.GetProperty("out"))),
          string.Join(",", Glossary.Words(JArr(c.GetProperty("in"))).OrderBy(x => x, StringComparer.Ordinal)),
          "");

foreach (var (c, i) in Iter("parse_student_label"))
{
    var (num, name) = RosterData.ParseStudentLabel(J(c.GetProperty("in")));
    Check("parse_student_label", i, string.Join("|", JArr(c.GetProperty("out"))),
          $"{num}|{name}", J(c.GetProperty("in")));
}

// ── PyRandom(Python random 재현) ───────────────────────────────────────
if (root.TryGetProperty("pyrandom", out var pr))
{
    var r1 = new PyRandom(42);
    string eG = string.Join(",", pr.GetProperty("getrandbits32").EnumerateArray().Select(x => x.GetUInt32()));
    string aG = string.Join(",", Enumerable.Range(0, 8).Select(_ => r1.GenrandUint32()));
    Check("pyrandom.getrandbits32", 0, eG, aG);

    var r2 = new PyRandom(42);
    var lst = Enumerable.Range(0, 12).ToList(); r2.Shuffle(lst);
    Check("pyrandom.shuffle12", 0,
          string.Join(",", pr.GetProperty("shuffle12").EnumerateArray().Select(x => x.GetInt32())),
          string.Join(",", lst));

    var r3 = new PyRandom(42);
    var pool = new[] { "a", "b", "c", "d", "e" };
    Check("pyrandom.choice5", 0,
          string.Join("", JArr(pr.GetProperty("choice5"))),
          string.Join("", Enumerable.Range(0, 8).Select(_ => r3.Choice(pool))));

    var r4 = new PyRandom(42);
    Check("pyrandom.random15", 0,
          string.Join(",", JArr(pr.GetProperty("random15"))),
          string.Join(",", Enumerable.Range(0, 4).Select(_ => r4.Random().ToString("F15", System.Globalization.CultureInfo.InvariantCulture))));

    var r5 = new PyRandom(123);
    Check("pyrandom.getrandbits32_s123", 0,
          string.Join(",", pr.GetProperty("getrandbits32_s123").EnumerateArray().Select(x => x.GetUInt32())),
          string.Join(",", Enumerable.Range(0, 4).Select(_ => r5.GenrandUint32())));
}

// ── combine_variants(RNG 조합) ─────────────────────────────────────────
foreach (var (c, i) in Iter("combine_variants"))
{
    var groups = c.GetProperty("groups").EnumerateArray()
        .Select(g => g.EnumerateArray().Select(x => x.GetString() ?? "").ToList()).ToList();
    var got = Variation.CombineVariants(groups, c.GetProperty("n").GetInt32());
    Check("combine_variants", i, string.Join("¶", JArr(c.GetProperty("out"))),
          string.Join("¶", got), $"n={c.GetProperty("n").GetInt32()}");
}

// ── compliance ─────────────────────────────────────────────────────────
foreach (var (c, i) in Iter("compliance_check"))
{
    string exp = string.Join("¶", c.GetProperty("out").EnumerateArray()
        .Select(t => string.Join("|", t.EnumerateArray().Select(x => x.GetString()))));
    string act = string.Join("¶", Compliance.Check(J(c.GetProperty("in")))
        .Select(t => $"{t.level}|{t.cat}|{t.token}"));
    Check("compliance_check", i, exp, act, J(c.GetProperty("in")));
}
foreach (var (c, i) in Iter("compliance_summary"))
    Check("compliance_summary", i, J(c.GetProperty("out")),
          Compliance.Summary(J(c.GetProperty("in"))), J(c.GetProperty("in")));

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
