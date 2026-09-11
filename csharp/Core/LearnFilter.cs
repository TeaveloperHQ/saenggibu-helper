namespace Saenggibu;

/// <summary>학습 대상 판별 — 이상한 데이터(숫자·학기·이름·메모 조각)가 학습 예시로 들어가지 않게.
/// ① 빠른 규칙 판별 → ② 언어 모델이 '생기부 기재 문장인지' 예/아니오 판별.</summary>
public static class LearnFilter
{
    /// <summary>1차(규칙): 15자 이상, 한글 8자 이상, 띄어쓰기 있음.</summary>
    public static bool LooksLikeSentence(string? t)
    {
        t = (t ?? "").Trim();
        return t.Length >= 15 && t.Count(ch => ch >= '가' && ch <= '힣') >= 8 && t.Contains(' ');
    }

    private const string JudgeSystem =
        "너는 문장 판별기다. 입력 글이 학교생활기록부에 기재하는 문장(교사가 학생의 활동·태도·역량·성장을 서술한 문장)인지 판단해 " +
        "'예' 또는 '아니오' 한 단어로만 답한다. 설명하지 않는다.";

    /// <summary>2차(언어 모델): 생기부 기재 문장이면 true, 아니면 false, 답을 해석할 수 없거나 오류면 null(규칙 판별만 따름).</summary>
    public static bool? IsRecordSentence(ILlmEngine engine, string areaTitle, string text)
    {
        string t = text.Trim();
        if (t.Length > 400) t = t[..400];
        string user =
            "예시)\n" +
            "글: 2026 1학기 3학년 2반 → 아니오\n" +
            "글: 김민수 010-1234-5678 보호자 연락 요망 → 아니오\n" +
            "글: 내일까지 수행평가 채점하고 회의 자료 챙기기 → 아니오\n" +
            "글: 모둠 탐구에서 친구들의 의견을 경청하고 조율하여 보고서를 완성하는 등 협업 역량이 돋보임. → 예\n\n" +
            $"항목: {areaTitle}\n글: {t}\n답:";
        string o;
        try { o = engine.Complete(JudgeSystem, user, 4, 0.0).Trim(); }
        catch { return null; }
        if (o.StartsWith("예")) return true;
        if (o.Contains("아니")) return false;
        return null;
    }
}
