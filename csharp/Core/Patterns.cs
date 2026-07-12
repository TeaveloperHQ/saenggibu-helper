using System.Text.RegularExpressions;

namespace Saenggibu;

/// <summary>
/// app/patterns.py 이식 중 결정론적 부분(classify/instruction). plan/_weighted_pick 은
/// rng 의존이라 후속(Python 호환 RNG).
/// </summary>
public static class Patterns
{
    private static readonly Regex EndEval = new(@"(보임|드러냄|돋보임|지님|발휘함|뛰어남|우수함|보여줌|엿보임|인상적임|강함)$", RegexOptions.Compiled);
    private static readonly Regex EndGrow = new(@"(키움|기름|넓힘|성장함|향상됨|높임|길러냄|확장함|더함|쌓음)$", RegexOptions.Compiled);
    private static readonly Regex EndObs = new(@"(설명함|발표함|수행함|기여함|경험함|체득함|참여함|완수함|제작함|작성함|정리함|조사함|탐구함|실천함|전달함|공유함|진행함|이끎|나눔|함)$", RegexOptions.Compiled);

    private static readonly (string pat, string nm)[] ConnPat =
    {
        ("을 통해", "통해"), ("를 통해", "통해"), ("는 등", "는등"),
        ("으로써", "으로써"), ("하여", "하여"), ("하며", "하며"), ("으며", "하며"),
    };
    private static readonly Regex CtxOpen = new(@"(에서|단원|활동|시간|과정에서)\s", RegexOptions.Compiled);
    private static readonly Regex SplitSents = new(@"(?<=[.])\s+", RegexOptions.Compiled);

    private static List<string> SplitSentsFn(string text) =>
        SplitSents.Split(text.Trim()).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    private static string EndingType(string last)
    {
        if (EndEval.IsMatch(last)) return "평가형";
        if (EndGrow.IsMatch(last)) return "성장형";
        if (EndObs.IsMatch(last)) return "관찰형";
        return "관찰형";
    }

    // DEFAULT_PROFILE (코퍼스 실측, 순서 보존)
    public static readonly Dictionary<string, (string k, double w)[]> DefaultProfile = new()
    {
        ["comp"] = new[] { ("단문", 0.52), ("2문장", 0.48) },
        ["end"] = new[] { ("평가형", 0.42), ("관찰형", 0.31), ("성장형", 0.27) },
        ["order"] = new[] { ("활동먼저", 0.80), ("맥락먼저", 0.20) },
        ["conn"] = new[] { ("하며", 0.50), ("하여", 0.16), ("통해", 0.16), ("는등", 0.08), ("으로써", 0.06), ("고", 0.04) },
    };

    private static readonly Dictionary<string, string> EndHint = new()
    {
        ["평가형"] = "끝을 역량 평가로 맺어라(예: ~하는 모습을 보임 / ~역량을 드러냄 / ~자세를 지님)",
        ["관찰형"] = "끝을 활동 서술로 맺어라(예: ~을 발표함 / ~에 기여함 / ~을 정리함)",
        ["성장형"] = "끝을 성장으로 맺어라(예: ~을 키움 / ~을 넓힘 / ~으로 성장함)",
    };
    private static readonly Dictionary<string, string> ConnHint = new()
    {
        ["하며"] = "절을 '~하며'로 이어", ["하여"] = "절을 '~하여'로 이어",
        ["통해"] = "'~을 통해'로 연결해", ["는등"] = "'~하는 등'으로 나열해",
        ["으로써"] = "'~함으로써'로 이어", ["고"] = "절을 '~하고'로 이어",
    };

    private static string WeightedPick((string k, double w)[] dist, PyRandom rng, ISet<string>? exclude = null)
    {
        var items = dist.Where(x => (exclude == null || !exclude.Contains(x.k)) && x.w > 0).ToList();
        if (items.Count == 0) items = dist.Where(x => x.w > 0).ToList();
        if (items.Count == 0) items = new List<(string, double)> { (dist[0].k, 1.0) };
        double tot = items.Sum(x => x.w);
        double r = rng.Random() * tot, acc = 0;
        foreach (var (k, w) in items) { acc += w; if (r <= acc) return k; }
        return items[^1].Item1;
    }

    /// <summary>app/patterns.py plan — n개의 서로 다른 구조 타깃.</summary>
    public static List<Dictionary<string, string>> Plan(int n, Dictionary<string, (string, double)[]> profile, PyRandom rng)
    {
        profile ??= DefaultProfile;
        var usedPairs = new Dictionary<(string, string), int>();
        var outp = new List<Dictionary<string, string>>();
        for (int i = 0; i < Math.Max(1, n); i++)
        {
            string comp = "", end = "";
            for (int a = 0; a < 6; a++)
            {
                comp = WeightedPick(profile.GetValueOrDefault("comp", DefaultProfile["comp"]), rng);
                end = WeightedPick(profile.GetValueOrDefault("end", DefaultProfile["end"]), rng);
                int cnt = usedPairs.GetValueOrDefault((comp, end), 0);
                int minv = usedPairs.Count > 0 ? usedPairs.Values.Min() : 0;
                if (cnt <= minv) break;
            }
            usedPairs[(comp, end)] = usedPairs.GetValueOrDefault((comp, end), 0) + 1;
            string order = WeightedPick(profile.GetValueOrDefault("order", DefaultProfile["order"]), rng);
            string conn = WeightedPick(profile.GetValueOrDefault("conn", DefaultProfile["conn"]), rng);
            outp.Add(new() { ["comp"] = comp, ["end"] = end, ["order"] = order, ["conn"] = conn });
        }
        return outp;
    }

    private static readonly string[] Axes = { "comp", "end", "order", "conn" };

    /// <summary>app/patterns.py analyze — 예시 묶음의 축별 빈도 프로파일(표본 적으면 기본값).
    /// set-union 키 순서는 Python이 비결정이므로 C#은 결정적 순서(기본값 우선). LLM 입력이라 무방.</summary>
    public static Dictionary<string, (string, double)[]> Analyze(IReadOnlyList<string> texts)
    {
        var counters = Axes.ToDictionary(a => a, _ => new Dictionary<string, int>());
        int n = 0;
        foreach (var t0 in texts)
        {
            var t = (t0 ?? "").Trim();
            if (t.Length == 0) continue;
            n++;
            var lab = Classify(t);
            foreach (var a in Axes)
                counters[a][lab[a]] = counters[a].GetValueOrDefault(lab[a], 0) + 1;
        }
        if (n < 8) return DefaultProfile.ToDictionary(kv => kv.Key, kv => kv.Value);
        var prof = new Dictionary<string, (string, double)[]>();
        foreach (var a in Axes)
        {
            int tot = counters[a].Values.Sum(); if (tot == 0) tot = 1;
            double w = Math.Min(1.0, n / 60.0);
            var keys = new List<string>();
            foreach (var (k, _) in DefaultProfile[a]) if (!keys.Contains(k)) keys.Add(k);   // 기본값 순서 우선
            foreach (var k in counters[a].Keys) if (!keys.Contains(k)) keys.Add(k);
            var def = DefaultProfile[a].ToDictionary(x => x.Item1, x => x.Item2);
            prof[a] = keys.Select(k => (k, w * (counters[a].GetValueOrDefault(k, 0) / (double)tot)
                + (1 - w) * def.GetValueOrDefault(k, 0.0))).ToArray();
        }
        return prof;
    }

    /// <summary>app/patterns.py instruction — 구조 타깃 → LLM 한 줄 지시.</summary>
    public static string Instruction(Dictionary<string, string> t)
    {
        string comp = t["comp"] == "단문" ? "한 문장으로 압축해" : "두 문장으로 나눠(활동 문장 + 평가/성장 문장),";
        string order = t["order"] == "맥락먼저" ? "'~단원에서/~활동에서'처럼 맥락으로 시작해 " : "";
        string conn = ConnHint.GetValueOrDefault(t["conn"], "절을 자연스럽게 이어");
        string end = EndHint.GetValueOrDefault(t["end"], "");
        return $"{order}{comp} {conn}, {end}";
    }

    public static Dictionary<string, string> Classify(string text)
    {
        text = (text ?? "").Trim();
        var sents = SplitSentsFn(text);
        string comp = sents.Count <= 1 ? "단문" : "2문장";
        var wsTokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string last = wsTokens.Length > 0
            ? text.TrimEnd('.').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[^1]
            : "";
        string end = EndingType(last);
        string first = sents.Count > 0 ? sents[0] : text;
        string head = first.Length > 22 ? first[..22] : first;
        string order = CtxOpen.IsMatch(head) ? "맥락먼저" : "활동먼저";
        string conn = ConnPat.FirstOrDefault(p => text.Contains(p.pat, StringComparison.Ordinal)).nm ?? "고";
        return new Dictionary<string, string>
        {
            ["comp"] = comp, ["end"] = end, ["order"] = order, ["conn"] = conn,
        };
    }
}
