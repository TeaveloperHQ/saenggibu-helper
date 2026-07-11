using System.Text.RegularExpressions;

namespace Saenggibu;

/// <summary>
/// app/memory_store.py 의 tokenize 이식 — 어절 토큰 + 한글 글자 bigram.
/// </summary>
public static class Tokenizer
{
    // _WORD_RE = [가-힣]+|[a-zA-Z]+|[0-9]+
    private static readonly Regex WordRe = new(@"[가-힣]+|[a-zA-Z]+|[0-9]+", RegexOptions.Compiled);
    private static readonly Regex HangulOnly = new(@"^[가-힣]+$", RegexOptions.Compiled);

    public static List<string> Tokenize(string text)
    {
        text = text.ToLowerInvariant();
        var tokens = new List<string>();
        foreach (Match m in WordRe.Matches(text))
        {
            var w = m.Value;
            tokens.Add(w);
            if (w.Length >= 2 && HangulOnly.IsMatch(w))
            {
                for (int i = 0; i < w.Length - 1; i++)
                    tokens.Add(w.Substring(i, 2));
            }
        }
        return tokens;
    }
}
