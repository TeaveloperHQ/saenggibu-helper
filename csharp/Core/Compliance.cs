using System.Text.RegularExpressions;

namespace Saenggibu;

/// <summary>app/compliance.py 이식 — 생기부 기재 불가/주의 항목 검사(순수 정규식).</summary>
public static class Compliance
{
    private static readonly (string level, string cat, Regex re)[] Rules =
    {
        ("block", "공인어학시험", new Regex(
            @"(TOEIC|TOEFL|TEPS|IELTS|OPIC|TOEIC\s*Speaking|HSK|JPT|JLPT|" +
            @"DELE|DELF|DALF|TORFL|TESTDAF|DSH|DSD|토익|토플|텝스|아이엘츠|" +
            @"한자능력검정|한자급수|실용한자)", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("block", "모의고사·수능성적", new Regex(
            @"(모의고사|전국연합\s*학력평가|학력평가\s*\d|수능\s*\d|" +
            @"백분위|석차\s*등급|원점수|등급컷)", RegexOptions.Compiled)),
        ("block", "논문·학회지·특허", new Regex(
            @"(논문|학회지|학술지|저널|특허|실용신안|상표\s*등록|" +
            @"디자인\s*등록|지식재산권|저서\s*출간|책\s*출간|출판)", RegexOptions.Compiled)),
        ("warn", "대회·수상·표창", new Regex(
            @"(표창장|감사장|공로상|수상\s*(함|한|하여|경력)|입상|" +
            @"경시\s*대회|올림피아드|대회\s*(참가|참여|출전|준비|나감)|" +
            @"(참가|참여|출전)\S*\s*대회)", RegexOptions.Compiled)),
        ("warn", "자격증 취득", new Regex(
            @"(자격증.{0,4}(취득|획득|딴|따)|자격을?\s*(취득|획득)|" +
            @"급수\s*(취득|인증)|기능사\s*자격|기사\s*자격)", RegexOptions.Compiled)),
        ("warn", "해외 활동", new Regex(
            @"(어학연수|해외\s*봉사|해외\s*연수|해외\s*활동|유학)", RegexOptions.Compiled)),
        ("warn", "장학금", new Regex(@"(장학금|장학생|장학\s*재단)", RegexOptions.Compiled)),
        ("warn", "대학·기관·상호명", new Regex(
            @"([가-힣A-Za-z]{2,}\s*대학교|[가-힣]{2,}\s*대학원|" +
            @"[가-힣A-Za-z]{2,}\s*학원|주식회사|㈜|\(주\))", RegexOptions.Compiled)),
        ("warn", "부모 정보", new Regex(
            @"(아버지|어머니|부모(님)?)\s*(가|는|의|께서)?\s*" +
            @"[가-힣]*\s*(대표|사장|교수|의사|변호사|공무원|회사|직장|근무|운영)", RegexOptions.Compiled)),
    };

    public static List<(string level, string cat, string token)> Check(string? text)
    {
        text ??= "";
        var outp = new List<(string, string, string)>();
        var seen = new HashSet<string>();
        foreach (var (level, cat, re) in Rules)
            foreach (Match m in re.Matches(text))
            {
                string token = m.Value.Trim();
                string key = $"{cat}:{token}";
                if (token.Length > 0 && seen.Add(key))
                    outp.Add((level, cat, token));
            }
        return outp;
    }

    public static string Summary(string? text)
    {
        var v = Check(text);
        if (v.Count == 0) return "";
        var blocks = v.Where(x => x.level == "block").Select(x => $"{x.cat}('{x.token}')").ToList();
        var warns = v.Where(x => x.level == "warn").Select(x => $"{x.cat}('{x.token}')").ToList();
        var parts = new List<string>();
        if (blocks.Count > 0) parts.Add("기재 불가: " + string.Join(", ", blocks));
        if (warns.Count > 0) parts.Add("확인 요망: " + string.Join(", ", warns));
        return string.Join(" / ", parts);
    }
}
