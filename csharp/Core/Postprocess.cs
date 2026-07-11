using System.Text;
using System.Text.RegularExpressions;

namespace Saenggibu;

/// <summary>
/// app/postprocess.py 이식 — 명사형 종결 강제 변환(결정론적). 접미사 치환표 순서 동일.
/// </summary>
public static class Postprocess
{
    // 긴 접미사 우선, 위에서부터 검사 (Python _SUFFIX_RULES 순서 그대로)
    private static readonly (string suf, string rep)[] SuffixRules =
    {
        ("하였다", "함"), ("하였음", "함"), ("했다", "함"), ("한다", "함"),
        ("하다", "함"),
        ("되었다", "됨"), ("되었음", "됨"), ("됐다", "됨"), ("됬다", "됨"),
        ("된다", "됨"), ("되다", "됨"),
        ("롭다", "로움"), ("답다", "다움"), ("럽다", "러움"), ("겁다", "거움"),
        ("갑다", "가움"), ("렵다", "려움"), ("쉽다", "쉬움"), ("깝다", "까움"),
        ("덥다", "더움"), ("춥다", "추움"), ("맵다", "매움"), ("곱다", "고움"),
        ("돕다", "도움"), ("굽다", "구움"),
        ("깨닫다", "깨달음"), ("듣다", "들음"),
        ("깨달았다", "깨달음"), ("깨달았음", "깨달음"),
        ("길렀다", "기름"), ("길렀음", "기름"), ("이르렀다", "이름"),
        ("냈다", "냄"), ("냈음", "냄"),
        ("웠다", "움"), ("줬다", "줌"), ("췄다", "춤"), ("졌다", "짐"),
        ("겼다", "김"), ("렸다", "림"), ("혔다", "힘"), ("폈다", "핌"),
        ("쳤다", "침"), ("뤘다", "룸"), ("셨다", "심"), ("켰다", "킴"),
        ("꼈다", "낌"),
        ("이었다", "임"), ("주었다", "줌"), ("이루었다", "이룸"),
        ("였다", "임"), ("였음", "임"),
        ("도왔다", "도움"), ("고왔다", "고움"),
        ("갔다", "감"), ("왔다", "옴"), ("봤다", "봄"), ("났다", "남"),
        ("섰다", "섬"), ("탔다", "탐"), ("샀다", "삼"), ("찼다", "참"),
        ("짰다", "짬"), ("썼다", "씀"),
        ("인다", "임"), ("낸다", "냄"), ("는다", "음"), ("ㄴ다", "ㅁ"),
        ("이다", "임"), ("있다", "있음"), ("없다", "없음"), ("같다", "같음"),
    };

    private static readonly string[] PastSuffixes = { "었다", "았다", "었음", "았음" };

    private static readonly Regex TailRe = new("[\\s.。!?…\"'’”」』）)\\]]*$", RegexOptions.Compiled);
    private static readonly Regex SemiColon = new(@"\s*[;:]\s*", RegexOptions.Compiled);
    private static readonly Regex SentSplit = new(@"(?<=[.。!?])(\s+)", RegexOptions.Compiled);
    private static readonly Regex NumMark = new(@"(?m)^\s*\d+\s*[).\]]\s*", RegexOptions.Compiled);

    private static readonly Regex PromptEcho = new(
        "위 키워드|철자를 바꾸지|그대로 정확히 사용|키워드/관찰|다듬을 초안|" +
        "한 줄에 하나|의미는 같되|새로운 정보를 지어|접속어로 시작|예시 형식|" +
        "핵심 내용을 항목별|문체/톤|분량\\s*:|과목\\s*:|\\{\\{|\\}\\}", RegexOptions.Compiled);

    private static string? NominalizeStem(string stem)
    {
        if (stem.Length == 0) return null;
        char last = stem[^1];
        if (!Hangul.IsSyllable(last)) return null;
        var (cho, jung, jong) = Hangul.Decompose(last);
        if (jong == "")
            return stem[..^1] + Hangul.Compose(cho, jung, "ㅁ");
        if (jong == "ㄹ")
            return stem[..^1] + Hangul.Compose(cho, jung, "ㄻ");
        return stem + "음";
    }

    private static string ConvertCore(string core)
    {
        foreach (var (suf, rep) in SuffixRules)
            if (core.EndsWith(suf, StringComparison.Ordinal))
                return core[..^suf.Length] + rep;
        foreach (var suf in PastSuffixes)
            if (core.EndsWith(suf, StringComparison.Ordinal))
            {
                var nom = NominalizeStem(core[..^suf.Length]);
                return nom ?? core;
            }
        var n = NominalizeStem(core[..^1]);
        return n ?? core;
    }

    public static string NominalizeSentence(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence)) return sentence;
        var m = TailRe.Match(sentence);
        string tail = m.Success ? m.Value : "";
        string core = sentence[..(sentence.Length - tail.Length)];
        if (core.Length == 0) return sentence;
        char last = core[^1];
        if (Hangul.HasMFinal(last)) return sentence;          // 이미 명사형
        if (!core.EndsWith("다", StringComparison.Ordinal)) return sentence;
        return ConvertCore(core) + tail;
    }

    public static string ToNominalEndings(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = SemiColon.Replace(text, ". ");
        var outLines = new List<string>();
        foreach (var line in text.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) { outLines.Add(line); continue; }
            var parts = SentSplit.Split(line);
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
                sb.Append(i % 2 == 0 ? NominalizeSentence(parts[i]) : parts[i]);
            outLines.Add(sb.ToString());
        }
        return string.Join("\n", outLines);
    }

    public static List<string> SplitVariants(string text)
    {
        var parts = NumMark.Split(text);
        return parts.Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
    }

    public static bool HasPromptEcho(string? text) => PromptEcho.IsMatch(text ?? "");

    public static string StripPromptEcho(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var m = PromptEcho.Match(text);
        if (m.Success) text = text[..m.Index];
        return text.Trim()
                   .Trim('"', '\'', '“', '”')
                   .Trim()
                   .TrimEnd(',', '·', '-', '—', '•', '/', ' ')
                   .Trim();
    }
}
