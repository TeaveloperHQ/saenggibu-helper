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
