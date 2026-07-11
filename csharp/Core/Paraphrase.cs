using System.Text.RegularExpressions;

namespace Saenggibu;

/// <summary>
/// 형태소 분석기(kiwi) 추상화 — 1단계에선 미구현(null 폴백). 2단계에서 P/Invoke로 구현.
/// app/spellcheck.py 의 _get_kiwi + kiwi.tokenize/join 자리.
/// </summary>
public interface IKiwi
{
    IReadOnlyList<(string form, string tag)> Tokenize(string text);
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
}
