using System.Text.RegularExpressions;

namespace Saenggibu;

/// <summary>
/// 형태소 분석기(kiwi) 추상화 — 1단계에선 미구현(null 폴백). 2단계에서 P/Invoke로 구현.
/// app/spellcheck.py 의 _get_kiwi + kiwi.tokenize/join 자리.
/// </summary>
public interface IKiwi
{
    IReadOnlyList<(string form, string tag)> Tokenize(string text);
    IReadOnlyList<(string form, string tag, int start)> TokenizeFull(string text);
    string Join(IReadOnlyList<(string form, string tag)> morphs);
}

/// <summary>
/// app/paraphrase.py 이식 중 **kiwi 비의존 순수 결정론** 부분만.
/// kiwi 의존(치환·재배열·마스킹·llm_paraphrase 등)은 2단계(IKiwi 구현 후).
/// </summary>
public static class Paraphrase
{
    // _fix_spacing
    private static readonly Regex NumKo = new(@"(\d)\s+([가-힣])", RegexOptions.Compiled);
    private static readonly Regex Compound = new(@"(\d[가-힣]+)\s+(\d)", RegexOptions.Compiled);
    private static readonly Regex Cv = new(@"([가-힣]*[어아])\s+(냄|남|줌|놓음|둠|봄|냈?음|감|옴|짐)", RegexOptions.Compiled);

    public static string FixSpacing(string s)
    {
        s = NumKo.Replace(s, "$1$2");
        s = Compound.Replace(s, "$1$2");
        s = Cv.Replace(s, "$1$2");
        return s;
    }

    // _clean_line
    private static readonly Regex BulletPrefix = new(@"^\s*(?:\d+[.)]|[-•*·▪]|[(\[]?\d+[)\]])\s*", RegexOptions.Compiled);
    private static readonly Regex SlashSep = new(@"\s*/\s*", RegexOptions.Compiled);
    private static readonly char[] QuoteChars = { '"', '\'', '“', '”', '‘', '’' };

    public static string CleanLine(string line)
    {
        line = line.Trim();
        line = BulletPrefix.Replace(line, "");
        line = SlashSep.Replace(line, ". ");
        return line.Trim().Trim(QuoteChars).Trim();
    }

    // _bigrams
    public static HashSet<string> Bigrams(string s)
    {
        s = s.Replace(" ", "");
        var outp = new HashSet<string>();
        for (int i = 0; i < s.Length - 1; i++)
            outp.Add(s.Substring(i, 2));
        return outp;
    }

    // _too_similar
    public static bool TooSimilar(string a, string b, double thresh = 0.90)
    {
        var A = Bigrams(a);
        var B = Bigrams(b);
        if (A.Count == 0 || B.Count == 0)
            return a.Replace(" ", "") == b.Replace(" ", "");
        int inter = A.Count(x => B.Contains(x));
        int union = A.Count + B.Count - inter;
        return (double)inter / union >= thresh;
    }

    // _is_eval_sent
    private static readonly Regex EvalSentRe = new(
        @"(력|심|성|감|역량|자세|태도|정신|의식|자질|리더십)\S*\s*" +
        @"(보임|돋보임|뛰어남|우수함|발휘|지님|엿보임|인상적|강함|높음|빠름)", RegexOptions.Compiled);

    public static bool IsEvalSent(string s) => EvalSentRe.IsMatch(s);

    // ── kiwi 형태소 기반 변형 엔진(결정론 부분) ──────────────────────────
    /// <summary>app/paraphrase.py _alternatives — 각 형태소 위치의 치환 후보(원형 포함).</summary>
    public static List<List<string>> Alternatives(IReadOnlyList<(string form, string tag)> morphs)
    {
        var opts = new List<List<string>>();
        int n = morphs.Count;
        for (int i = 0; i < n; i++)
        {
            var (form, tag) = morphs[i];
            var alts = new List<string> { form };
            bool compound = i + 2 < n && morphs[i + 1].tag == "EC"
                && (morphs[i + 1].form == "어" || morphs[i + 1].form == "아")
                && (morphs[i + 2].tag is "VV" or "VA" or "VX");
            if ((tag is "VV" or "VA") && !compound && ParaphraseData.VerbMap.TryGetValue(form, out var v))
                alts.AddRange(v);
            else if (tag == "NNG" && i + 1 < n && morphs[i + 1].form == "하"
                     && (morphs[i + 1].tag is "XSV" or "XSA")
                     && ParaphraseData.PredMap.TryGetValue(form, out var p))
                alts.AddRange(p);
            else if (tag == "NNG" && ParaphraseData.NounMap.TryGetValue(form, out var no))
                alts.AddRange(no);
            else if (tag == "EC" && ParaphraseData.Ec.TryGetValue(form, out var e))
                alts.AddRange(e);
            opts.Add(alts);
        }
        return opts;
    }

    /// <summary>app/paraphrase.py _reorder — 안전 연결어미(고·며·여)에서 절을 나눠 순서 셔플.</summary>
    public static List<(string form, string tag)> Reorder(
        IReadOnlyList<(string form, string tag)> morphs, PyRandom rng)
    {
        var clauses = new List<(List<(string, string)> cl, bool nf)>();
        var cur = new List<(string, string)>();
        foreach (var m in morphs)
        {
            cur.Add(m);
            if (m.tag == "EC" && ParaphraseData.SafeConn.Contains(m.form))
            { clauses.Add((cur, true)); cur = new List<(string, string)>(); }
        }
        if (cur.Count > 0) clauses.Add((cur, false));
        var nonfinal = clauses.Where(c => c.nf).Select(c => c.cl).ToList();
        var final = clauses.Where(c => !c.nf).Select(c => c.cl).ToList();
        if (nonfinal.Count < 2) return morphs.ToList();
        var order = nonfinal.ToList();
        rng.Shuffle(order);
        if (SameOrder(order, nonfinal)) return morphs.ToList();
        var flat = new List<(string, string)>();
        foreach (var c in order) flat.AddRange(c);
        foreach (var c in final) flat.AddRange(c);
        return flat;
    }

    private static bool SameOrder(List<List<(string, string)>> a, List<List<(string, string)>> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Count != b[i].Count) return false;
            for (int j = 0; j < a[i].Count; j++)
                if (a[i][j] != b[i][j]) return false;
        }
        return true;
    }

    /// <summary>app/paraphrase.py _seed_of — 문장별 결정론 시드.</summary>
    public static long SeedOf(string text)
    {
        long s = 0;
        int len = Math.Min(64, text.Length);
        for (int i = 0; i < len; i++) s += (long)text[i] * (i + 1);
        return s & 0xFFFFFFFFL;
    }

    /// <summary>app/paraphrase.py _subject_evals — 과목명 유연 매칭.</summary>
    public static List<string> SubjectEvals(string subject)
    {
        subject = (subject ?? "").Trim();
        if (subject.Length == 0) return new List<string>();
        foreach (var kv in ParaphraseData.SubjectEval)
            if (subject.Contains(kv.Key, StringComparison.Ordinal) || kv.Key.Contains(subject, StringComparison.Ordinal))
                return kv.Value.ToList();
        return new List<string>();
    }

    private static string TagBase(string tag) => tag.Split('-')[0];

    /// <summary>app/paraphrase.py _clause_form — 명사형 종결을 연결어미 절로.</summary>
    public static List<(string form, string tag)> ClauseForm(
        IReadOnlyList<(string form, string tag)> morphs, PyRandom rng)
    {
        var ms = morphs.ToList();
        while (ms.Count > 0 && TagBase(ms[^1].tag) is "SF" or "SP" or "SE" or "SS" or "ETN" or "EF")
            ms.RemoveAt(ms.Count - 1);
        if (ms.Count > 0 && TagBase(ms[^1].tag) is "VV" or "VA" or "VX" or "XSV" or "XSA")
        {
            var (stem, tag) = ms[^1];
            var ecs = new List<string> { "며", "고" };
            if (TagBase(tag) == "XSV" || stem.EndsWith("하", StringComparison.Ordinal))
                ecs = new List<string> { "며", "고", "여" };
            ms.Add((rng.Choice(ecs), "EC"));
        }
        return ms;
    }

    /// <summary>app/paraphrase.py _sentence_variants — 한 문장의 본문 변형(동의어·연결어미·부사).</summary>
    public static List<string> SentenceVariants(string sent, int k, PyRandom rng, bool adverbs, IKiwi kiwi)
    {
        List<(string form, string tag)> morphs;
        try { morphs = kiwi.Tokenize(sent).ToList(); }
        catch { return new List<string> { sent }; }
        var opts = Alternatives(morphs);
        var varPos = Enumerable.Range(0, opts.Count).Where(i => opts[i].Count > 1).ToList();
        var outp = new List<string>();
        var seen = new HashSet<string>();

        void Add(IReadOnlyList<(string, string)> ms)
        {
            string s;
            try { s = FixSpacing(Postprocess.ToNominalEndings(kiwi.Join(ms))); }
            catch { return; }
            string key = s.Replace(" ", "");
            if (key.Length > 0 && seen.Add(key)) outp.Add(s);
        }

        Add(morphs);
        foreach (var i in varPos)
            foreach (var alt in opts[i].Where(a => a != morphs[i].form).Take(2))
            {
                var ch = morphs.ToList();
                ch[i] = (alt, morphs[i].tag);
                Add(ch);
            }

        if (IsEvalSent(sent))
        {
            foreach (var bas in outp.ToList())
            {
                string b = bas.TrimEnd('.', ' ');
                foreach (var (pat, reps) in ParaphraseData.EvalPredSwap)
                    if (b.EndsWith(pat, StringComparison.Ordinal))
                    {
                        foreach (var rep in reps)
                        {
                            string s2 = b[..^pat.Length] + rep;
                            if (seen.Add(s2.Replace(" ", ""))) outp.Add(s2);
                        }
                        break;
                    }
            }
        }

        if (adverbs && outp.Count < k)
        {
            var advPts = Enumerable.Range(0, morphs.Count).Where(i =>
                morphs[i].tag == "NNG" && i + 1 < morphs.Count && morphs[i + 1].form == "하"
                && (morphs[i + 1].tag is "XSV" or "XSA")).ToList();
            var advs = ParaphraseData.Adverbs.ToList();
            rng.Shuffle(advs);
            foreach (var i in advPts)
                foreach (var adv in advs)
                {
                    if (outp.Count >= k + 2) break;
                    var ms = morphs.Take(i).Append((adv, "MAG")).Concat(morphs.Skip(i)).ToList();
                    Add(ms);
                }
        }
        return outp.Take(Math.Max(k, 1)).ToList();
    }

    private static readonly HashSet<string> NounTags = new() { "NNG", "NNP", "NNB", "SN", "SL", "SH", "NR" };
    private static readonly string[] Masks =
        { "갑물질", "을물질", "병물질", "정물질", "무물질", "기물질", "경물질", "신물질",
          "해물질", "수물질", "차물질", "토물질" };
    private static readonly Regex SplitDotRe = new(@"(?<=[.])\s+", RegexOptions.Compiled);

    public static List<string> SplitSentsDot(string text) =>
        SplitDotRe.Split(text.Trim()).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    /// <summary>app/paraphrase.py _auto_compounds — 붙여 쓴 복합명사를 원문 그대로 추출.</summary>
    public static List<string> AutoCompounds(string sentence, IKiwi kiwi)
    {
        IReadOnlyList<(string form, string tag, int start)> toks;
        try { toks = kiwi.TokenizeFull(sentence); }
        catch { return new List<string>(); }
        var comps = new List<string>();
        int i = 0, n = toks.Count;
        while (i < n)
        {
            if (NounTags.Contains(toks[i].tag))
            {
                int j = i;
                while (j + 1 < n && NounTags.Contains(toks[j + 1].tag)
                       && toks[j + 1].start == toks[j].start + toks[j].form.Length)
                    j++;
                if (j > i)
                {
                    int start = toks[i].start;
                    int end = toks[j].start + toks[j].form.Length;
                    string surf = sentence.Substring(start, end - start);
                    if (surf.Length >= 2) comps.Add(surf);
                }
                i = j + 1;
            }
            else i++;
        }
        return comps;
    }

    /// <summary>app/paraphrase.py _mask_terms — 등록 용어+복합명사를 플레이스홀더로. (순서 보존 매핑)</summary>
    public static (string masked, List<(string ph, string orig)> mapping) MaskTerms(
        string sentence, IKiwi kiwi, IReadOnlyCollection<string> glossaryTerms)
    {
        var targets = new List<string>();
        foreach (var t in glossaryTerms.OrderByDescending(x => x.Length).ThenBy(x => x, StringComparer.Ordinal))
            if (t.Length > 0 && !targets.Contains(t)) targets.Add(t);
        var comps = new HashSet<string>(AutoCompounds(sentence, kiwi));
        foreach (var c in comps.OrderByDescending(x => x.Length).ThenBy(x => x, StringComparer.Ordinal))
            if (!targets.Contains(c)) targets.Add(c);

        string masked = sentence;
        var mapping = new List<(string, string)>();
        int i = 0;
        foreach (var t in targets)
        {
            if (t.Length > 0 && masked.Contains(t, StringComparison.Ordinal) && i < Masks.Length)
            {
                string ph = Masks[i];
                masked = masked.Replace(t, ph);
                mapping.Add((ph, t));
                i++;
            }
        }
        return (masked, mapping);
    }

    /// <summary>app/paraphrase.py _unmask — 글자 사이 공백 허용해 원어 복원.</summary>
    public static string Unmask(string text, List<(string ph, string orig)> mapping)
    {
        foreach (var (ph, orig) in mapping)
        {
            var pat = string.Join(@"\s*", ph.Select(c => Regex.Escape(c.ToString())));
            text = Regex.Replace(text, pat, orig.Replace("$", "$$"));
        }
        return text;
    }

    /// <summary>app/paraphrase.py _merge_forms — 쪼갠 문장들을 접속사로 병합(마지막 절만 명사형).</summary>
    public static List<string> MergeForms(IReadOnlyList<string> msents, PyRandom rng, IKiwi kiwi, int rounds = 8)
    {
        if (msents.Count < 2) return new List<string>();
        List<List<(string form, string tag)>> morphset;
        try { morphset = msents.Select(s => kiwi.Tokenize(s).ToList()).ToList(); }
        catch { return new List<string>(); }
        var outp = new List<string>();
        var seen = new HashSet<string>();
        for (int r = 0; r < rounds; r++)
        {
            var parts = new List<List<(string, string)>>();
            for (int idx = 0; idx < morphset.Count; idx++)
                parts.Add(idx < morphset.Count - 1 ? ClauseForm(morphset[idx], rng) : morphset[idx].ToList());
            var flat = parts.SelectMany(p => p).ToList();
            string surf;
            try { surf = FixSpacing(Postprocess.ToNominalEndings(kiwi.Join(flat))); }
            catch { continue; }
            if (surf.Replace(" ", "").Length > 0 && seen.Add(surf.Replace(" ", ""))) outp.Add(surf);
        }
        return outp;
    }

    /// <summary>app/paraphrase.py _recombine_paraphrase — 다문장 입력을 문장별 변형해 재조합.</summary>
    public static List<string> RecombineParaphrase(string sentence, int n, IKiwi kiwi,
        IReadOnlyCollection<string> glossaryTerms, IReadOnlyList<string> rejected)
    {
        var (masked, mapping) = MaskTerms(sentence, kiwi, glossaryTerms);
        var msents = SplitSentsDot(masked);
        var rng = new PyRandom(SeedOf(sentence));
        var pools = new List<List<string>>();
        foreach (var ms in msents)
        {
            bool ev = IsEvalSent(ms);
            pools.Add(SentenceVariants(ms, ev ? 6 : 3, rng, ev, kiwi));
        }
        var results = new List<string>();
        var seen = new HashSet<string> { sentence.Replace(" ", "") };
        int tries = 0, limit = n * 150;
        double sim = 0.965;
        while (results.Count < n && tries < limit)
        {
            tries++;
            var chosen = pools.Select(p => rng.Choice(p)).ToList();
            string combo;
            if (chosen.Count >= 2 && rng.Random() < 0.55)
            {
                var mv = MergeForms(chosen, rng, kiwi, 1);
                combo = mv.Count > 0 ? mv[0] : string.Join(" ", chosen);
            }
            else combo = string.Join(" ", chosen);
            string full = Unmask(combo, mapping);
            string key = full.Replace(" ", "");
            if (seen.Contains(key)) continue;
            if (TooSimilar(full, sentence, 0.985)) continue;
            if (rejected.Any(rj => TooSimilar(full, rj, 0.9))) continue;
            if (results.Any(r => TooSimilar(full, r, sim))) continue;
            seen.Add(key);
            results.Add(full);
            if (tries > limit * 0.6 && results.Count < n) sim = 0.99;
        }
        return results.Take(n).ToList();
    }

    /// <summary>app/paraphrase.py _split_clauses — 절 단위(고·며·여)로 쪼개 명사형으로.</summary>
    public static List<string> SplitClauses(string text, IKiwi kiwi)
    {
        List<(string form, string tag)> toks;
        try { toks = kiwi.Tokenize(text).ToList(); }
        catch { return new List<string>(); }
        var clauses = new List<List<(string, string)>>();
        var cur = new List<(string, string)>();
        foreach (var m in toks)
        {
            cur.Add(m);
            if (m.tag == "EC" && ParaphraseData.SafeConn.Contains(m.form)) { clauses.Add(cur); cur = new(); }
        }
        if (cur.Count > 0) clauses.Add(cur);
        var outp = new List<string>();
        foreach (var c in clauses)
        {
            var cc = (c.Count > 0 && c[^1].Item2 == "EC") ? c.Take(c.Count - 1).ToList() : c;
            if (cc.Count > 0 && (cc[^1].Item2 is "VV" or "VA" or "VX" or "XSV" or "XSA"))
                cc = cc.Append(("ᆷ", "ETN")).ToList();
            string surf;
            try { surf = FixSpacing(Postprocess.ToNominalEndings(kiwi.Join(cc))).Trim(); }
            catch { continue; }
            if (surf.Length >= 4 && surf.Length <= 40) outp.Add(surf);
        }
        return outp;
    }

    private static string BalanceBody(string c)
    {
        string s = c.Split('.')[0];
        foreach (var adv in ParaphraseData.Adverbs) s = s.Replace(adv, "");
        s = s.Replace(" ", "");
        return s.Length <= 26 ? s : s[..26];
    }

    /// <summary>app/paraphrase.py _balance_by_structure — 구조 그룹 라운드로빈(본문 다양성 우선).</summary>
    public static List<string> BalanceByStructure(List<string> cands, int n, PyRandom rng)
    {
        var groups = new Dictionary<(string, string), List<(string c, string body)>>();
        var order = new List<(string, string)>();
        foreach (var c in cands)
        {
            var lab = Patterns.Classify(c);
            var st = (lab["comp"], lab["end"]);
            if (!groups.ContainsKey(st)) { groups[st] = new(); order.Add(st); }
            groups[st].Add((c, BalanceBody(c)));
        }
        rng.Shuffle(order);
        foreach (var st in order) rng.Shuffle(groups[st]);
        var outp = new List<string>();
        var seenBody = new HashSet<string>();
        bool progressed = true;
        while (outp.Count < n && progressed)
        {
            progressed = false;
            foreach (var st in order)
            {
                var g = groups[st];
                if (g.Count == 0) continue;
                int pick = g.FindIndex(x => !seenBody.Contains(x.body));
                if (pick < 0) pick = 0;
                var (c, body) = g[pick]; g.RemoveAt(pick);
                outp.Add(c); seenBody.Add(body); progressed = true;
                if (outp.Count >= n) break;
            }
        }
        return outp.Take(n).ToList();
    }

    // ── LLM 출력 검증 게이트(_valid) + 의존 함수 ──────────────────────────
    private static readonly Regex Latin = new(@"[A-Za-z]", RegexOptions.Compiled);
    private static readonly Regex HangulRe = new(@"[가-힣]", RegexOptions.Compiled);
    private static readonly Regex BadEnd = new(
        @"(으로써|으로서|하며|으며|하고|하여|면서|지만|거나|든지|어서|아서|도록|는데|니까)\.?$", RegexOptions.Compiled);
    private static readonly Regex BadNounEnd = new(
        @"(모습|태도|자세|면모|마음|능력|역량|자질|소임|역할|안목|감각|점|자신감|리더십|" +
        @"실력|사고력|논리력|창의력|통찰력|집중력|판단력|표현력|이해력|응용력|" +
        @"관찰력|발표력|어휘력|독해력|문해력|상상력|기억력|추진력|실행력)[함음임]\.?$", RegexOptions.Compiled);
    private static readonly Regex DrangRe = new(@"드러[움임넴]", RegexOptions.Compiled);

    /// <summary>app/paraphrase.py _fix_josa_ro — 받침 명사(ㄹ 제외) 뒤 '로'→'으로'.</summary>
    public static string FixJosaRo(string text, IKiwi kiwi)
    {
        if (!text.Contains('로')) return text;
        IReadOnlyList<(string form, string tag, int start)> toks;
        try { toks = kiwi.TokenizeFull(text); }
        catch { return text; }
        var edits = new List<int>();
        foreach (var t in toks)
            if (t.form == "로" && t.start > 0)
            {
                char prev = text[t.start - 1];
                if (prev >= '가' && prev <= '힣')
                {
                    int jong = (prev - 0xAC00) % 28;
                    if (jong != 0 && jong != 8) edits.Add(t.start);
                }
            }
        foreach (var pos in edits.OrderByDescending(x => x))
            text = text[..pos] + "으" + text[pos..];
        return text;
    }

    public static bool MalformedEnding(string v, IKiwi kiwi)
    {
        if (DrangRe.IsMatch(v)) return true;
        List<(string form, string tag)> toks;
        try { toks = kiwi.Tokenize(v).ToList(); }
        catch { return false; }
        for (int i = 0; i < toks.Count - 1; i++)
        {
            var cur = toks[i]; var nxt = toks[i + 1];
            if (nxt.form == "하" && (nxt.tag is "XSV" or "VV"))
            {
                if (cur.tag == "XSN") return true;
                if ((cur.tag is "NNG" or "NNP") && cur.form.Length > 0)
                {
                    char c = cur.form[^1];
                    if (c >= '가' && c <= '힣' && (c - 0xAC00) % 28 == 16) return true;
                }
            }
            if (cur.form == "드럽") return true;
        }
        return false;
    }

    public static bool StartsConnective(string v, IKiwi kiwi)
    {
        var parts = v.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        List<(string form, string tag)> toks;
        try { toks = kiwi.Tokenize(parts[0]).ToList(); }
        catch { return false; }
        return toks.Count > 0 && toks[^1].tag == "EC";
    }

    /// <summary>app/paraphrase.py _nouns — (전체 보존명사, 필수명사).</summary>
    public static (List<string> allk, List<string> crit) Nouns(
        string sentence, IKiwi kiwi, IReadOnlyCollection<string> glossaryTerms)
    {
        var allk = new List<string>(); var crit = new List<string>();
        List<(string form, string tag)> toks;
        try { toks = kiwi.Tokenize(sentence).ToList(); }
        catch { toks = new(); }
        int n = toks.Count;
        for (int i = 0; i < n; i++)
        {
            var (form, tag) = toks[i];
            if (tag == "NNP") { allk.Add(form); crit.Add(form); }
            else if (tag == "NNG" && form.Length >= 2)
            {
                var nxt = i + 1 < n ? toks[i + 1] : default;
                if (!(i + 1 < n && nxt.form == "하" && (nxt.tag is "XSV" or "XSA")))
                    allk.Add(form);
            }
        }
        string flat = sentence.Replace(" ", "");
        foreach (var t in glossaryTerms)
            if (flat.Contains(t.Replace(" ", ""), StringComparison.Ordinal)) { allk.Add(t); crit.Add(t); }
        return (Dedup(allk), Dedup(crit));
    }

    private static List<string> Dedup(IEnumerable<string> items)
    {
        var seen = new HashSet<string>(); var outp = new List<string>();
        foreach (var s in items) if (seen.Add(s)) outp.Add(s);
        return outp;
    }

    /// <summary>app/paraphrase.py _valid — LLM 변형 검증(용어보존·비문·표류·유사도).</summary>
    public static bool Valid(string v, IReadOnlyList<string> allk, IReadOnlyList<string> crit,
        string original, IKiwi kiwi, IReadOnlyCollection<string> glossaryTerms, double simThresh = 0.95)
    {
        if (string.IsNullOrEmpty(v) || (Latin.IsMatch(v) && HangulRe.IsMatch(v))) return false;
        if (BadEnd.IsMatch(v)) return false;
        if (BadNounEnd.IsMatch(v.TrimEnd('.', ' '))) return false;
        if (MalformedEnding(v, kiwi)) return false;
        if (StartsConnective(v, kiwi)) return false;
        if (ParaphraseData.Meta.Any(w => v.Contains(w, StringComparison.Ordinal))) return false;
        if (ParaphraseData.DriftWords.Any(w => v.Contains(w, StringComparison.Ordinal)
            && !original.Contains(w, StringComparison.Ordinal))) return false;
        if (TooSimilar(v, original, simThresh)) return false;
        int baseLen = original.Replace(" ", "").Length;
        int vlen = v.Replace(" ", "").Length;
        if (!(baseLen * 0.5 <= vlen && vlen <= baseLen * 2.6)) return false;
        string norm = v.Replace(" ", "");
        if (crit.Any(t => !norm.Contains(t.Replace(" ", ""), StringComparison.Ordinal))) return false;
        var allow = new HashSet<string>(allk);
        allow.UnionWith(ParaphraseData.GenericNouns);
        allow.UnionWith(ParaphraseData.EvalNouns);
        foreach (var nn in allk)
            if (ParaphraseData.NounMap.TryGetValue(nn, out var syn)) allow.UnionWith(syn);
        if (Nouns(v, kiwi, glossaryTerms).allk.Count(x => !allow.Contains(x)) > 1) return false;
        int missing = allk.Count(t => !norm.Contains(t.Replace(" ", ""), StringComparison.Ordinal)
            && !(ParaphraseData.NounMap.TryGetValue(t, out var s) && s.Any(x => norm.Contains(x, StringComparison.Ordinal))));
        return missing <= Math.Max(1, allk.Count / 5);
    }

    /// <summary>app/paraphrase.py _build_system — LLM용 시스템 프롬프트.</summary>
    public static string BuildSystem(IReadOnlyList<string> preserve)
    {
        string kept = preserve.Count > 0 ? string.Join(", ", preserve) : "(없음)";
        return "너는 학교생활기록부 문장을 '같은 의미의 다른 표현'으로 바꿔 쓰는 도우미다. " +
            "규칙을 반드시 지킨다:\n" +
            "1. 문장의 사실을 절대 바꾸지 않는다. 원문에 없는 새로운 활동·소재를 지어내지 않는다.\n" +
            $"2. 다음 명사는 철자 그대로 반드시 유지한다. 빼거나 다른 말로 바꾸지 않는다: {kept}\n" +
            "3. 서술어·조사·어미·어순을 바꾸거나, 부사(꼼꼼히·적극적으로·스스로 등)·생기부 " +
            "관용 표현(~하는 모습을 보임, ~태도가 돋보임 등)을 덧붙여 문장을 서로 다르게 만든다.\n" +
            "4. 모든 문장은 명사형 종결(~함, ~임, ~보임, ~지님)로 완결한다. 문장을 중간에 끊지 않는다.\n" +
            "5. 주어·학생 호칭('학생은' 등)을 쓰지 않는다.\n" +
            "6. 한 줄에 한 문장씩, 요청한 개수만큼만 출력한다. 번호·설명·따옴표 없이 본문만.\n\n" +
            "예) 원문: 산과 염기 단원에서 지시약 실험을 직접 설계하고 결과를 그래프로 정리함\n" +
            "바꾼 문장:\n" +
            "산과 염기 단원에서 지시약 실험을 스스로 계획하고 결과를 그래프로 나타냄\n" +
            "산과 염기 단원의 지시약 실험을 직접 구성하여 그 결과를 그래프로 정리해 봄\n" +
            "(유지된 명사: 산, 염기, 단원, 지시약, 실험, 결과, 그래프 — 서술어만 바뀜)";
    }

    private struct PoolItem { public string Kind; public List<(string, string)>? Ms; public string? Text; }

    /// <summary>app/paraphrase.py _mechanical — 사전·어미·어순 기반 결정론 변형(LLM 폴백 안전판).</summary>
    public static List<string> Mechanical(string sentence, int n, IKiwi kiwi, int seed = 42, string subject = "")
    {
        sentence = (sentence ?? "").Trim();
        if (sentence.Length == 0) return new List<string>();
        List<(string form, string tag)> morphs;
        try { morphs = kiwi.Tokenize(sentence).ToList(); }
        catch { return new List<string> { sentence }; }
        var opts = Alternatives(morphs);
        var varPos = Enumerable.Range(0, opts.Count).Where(i => opts[i].Count > 1).ToList();
        bool canReorder = morphs.Count(m => m.tag == "EC") >= 2;
        var rng = new PyRandom(seed);
        var results = new List<string>();
        var seen = new HashSet<string>();

        void AddSurf(string surf)
        {
            surf = FixSpacing(Postprocess.ToNominalEndings(surf));
            string key = surf.Replace(" ", "");
            if (key.Length > 0 && seen.Add(key)) results.Add(surf);
        }
        void Add(List<(string, string)> ms) { try { AddSurf(kiwi.Join(ms)); } catch { } }

        Add(morphs);
        var evals = ParaphraseData.EvalClauses.Concat(SubjectEvals(subject)).ToList();
        var poolPri = new List<PoolItem>();
        var pool = new List<PoolItem>();

        bool AdverbialBefore(int i)
        {
            if (i == 0) return false;
            var (f, tg) = morphs[i - 1];
            return tg == "MAG" || f == "으로" || f == "로"
                || (f.Length > 0 && (f[^1] == '히' || f[^1] == '게' || f[^1] == '이'));
        }
        var advPoints = Enumerable.Range(0, morphs.Count).Where(i =>
            morphs[i].tag == "NNG" && i + 1 < morphs.Count && morphs[i + 1].form == "하"
            && (morphs[i + 1].tag is "XSV" or "XSA") && !AdverbialBefore(i)).ToList();
        foreach (var i in advPoints)
            foreach (var adv in ParaphraseData.Adverbs)
                pool.Add(new PoolItem { Kind = "m", Ms = morphs.Take(i).Append((adv, "MAG")).Concat(morphs.Skip(i)).ToList() });

        // ② 관용 표현 + 평가절 부착
        int? eidx = null;
        for (int i = morphs.Count - 1; i >= 0; i--)
            if (morphs[i].tag is "ETN" or "EF") { eidx = i; break; }
        string? baseC = null;
        if (eidx is int e && e != 0 && (morphs[e - 1].tag is "VV" or "VA" or "XSV" or "VX"))
        {
            var et = morphs[e - 1].tag == "VA" ? ("ᆫ", "ETM") : ("는", "ETM");
            string? bas = null;
            try { bas = FixSpacing(kiwi.Join(morphs.Take(e).Append(et).ToList())); } catch { }
            if (bas != null)
                foreach (var idm in ParaphraseData.Idioms)
                    pool.Add(new PoolItem { Kind = "s", Text = bas + " " + idm });
            try
            {
                baseC = FixSpacing(kiwi.Join(morphs.Take(e).Append(("며", "EC")).ToList()));
                foreach (var ev in evals) pool.Add(new PoolItem { Kind = "s", Text = baseC + " " + ev });
            }
            catch { baseC = null; }
        }

        if (morphs.Count(m => m.tag == "EC" && ParaphraseData.SafeConn.Contains(m.form)) >= 2)
        {
            var clauses = SplitClauses(sentence, kiwi);
            if (clauses.Count >= 2) poolPri.Add(new PoolItem { Kind = "s", Text = string.Join(". ", clauses) });
        }

        string obs = FixSpacing(Postprocess.ToNominalEndings(kiwi.Join(morphs))).TrimEnd('.', ' ');
        foreach (var ev in evals)
        {
            string conn = ParaphraseData.EvalConnectors[rng.RandRange(ParaphraseData.EvalConnectors.Length)];
            pool.Add(new PoolItem { Kind = "two", Text = $"{obs}. {conn}{ev}" });
        }

        // ③-a 각 치환 위치 반드시 바꾼 변형
        foreach (var i in varPos)
            foreach (var alt in opts[i].Where(a => a != morphs[i].form).Take(2))
            {
                var chosen = morphs.ToList();
                chosen[i] = (alt, morphs[i].tag);
                poolPri.Add(new PoolItem { Kind = "m", Ms = chosen });
            }
        // ③-b 다중 치환 + 재배열
        if (varPos.Count > 0 || canReorder)
            for (int r = 0; r < n * 4; r++)
            {
                var chosen = morphs.ToList();
                foreach (var i in varPos) chosen[i] = (rng.Choice(opts[i]), morphs[i].tag);
                if (canReorder && rng.Random() < 0.55) chosen = Reorder(chosen, rng);
                pool.Add(new PoolItem { Kind = "m", Ms = chosen });
            }

        rng.Shuffle(poolPri);
        rng.Shuffle(pool);
        int cap = Math.Max(n * 4, 24);
        foreach (var it in poolPri.Concat(pool))
        {
            if (results.Count >= cap) break;
            if (it.Kind == "m") Add(it.Ms!); else AddSurf(it.Text!);
        }
        return BalanceByStructure(results, n, rng);
    }
}
