namespace Saenggibu;

/// <summary>
/// 한글 자모 분해/조합 — app/postprocess.py 의 _decompose/_compose/_is_syllable 이식.
/// </summary>
public static class Hangul
{
    private const int Base = 0xAC00;
    private const string Cho = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
    private const string Jung = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ";
    private static readonly string[] Jong =
    {
        "", "ㄱ", "ㄲ", "ㄳ", "ㄴ", "ㄵ", "ㄶ", "ㄷ", "ㄹ", "ㄺ", "ㄻ",
        "ㄼ", "ㄽ", "ㄾ", "ㄿ", "ㅀ", "ㅁ", "ㅂ", "ㅄ", "ㅅ", "ㅆ",
        "ㅇ", "ㅈ", "ㅊ", "ㅋ", "ㅌ", "ㅍ", "ㅎ"
    };

    public static bool IsSyllable(char ch) => ch >= 0xAC00 && ch <= 0xD7A3;

    public static (string cho, string jung, string jong) Decompose(char ch)
    {
        int code = ch - Base;
        int cho = code / 588;
        int jung = (code % 588) / 28;
        int jong = code % 28;
        return (Cho[cho].ToString(), Jung[jung].ToString(), Jong[jong]);
    }

    public static char Compose(string cho, string jung, string jong)
    {
        int ci = Cho.IndexOf(cho, StringComparison.Ordinal);
        int ji = Jung.IndexOf(jung, StringComparison.Ordinal);
        int ki = Array.IndexOf(Jong, jong);
        return (char)(Base + ci * 588 + ji * 28 + ki);
    }

    /// <summary>음절 받침이 ㅁ/ㄻ 이면 이미 명사형으로 본다.</summary>
    public static bool HasMFinal(char ch)
    {
        if (!IsSyllable(ch)) return false;
        var (_, _, jong) = Decompose(ch);
        return jong == "ㅁ" || jong == "ㄻ";
    }
}
