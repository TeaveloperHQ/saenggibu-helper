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
}
