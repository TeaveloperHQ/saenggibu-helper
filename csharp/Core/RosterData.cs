namespace Saenggibu;

/// <summary>app/roster_data.py 이식 중 순수 부분(parse_student_label). JSON 로스터 IO는 후속.</summary>
public static class RosterData
{
    /// <summary>'학번 이름' 표시명 → (학번, 이름). 학번 없으면 ("", 이름).</summary>
    public static (string num, string name) ParseStudentLabel(string? label)
    {
        string s = (label ?? "").Trim();
        if (s.Length == 0) return ("", "");
        // Python str.split(None, 1): 첫 공백에서 2조각(뒷조각 leading 공백 제거)
        int i = 0;
        while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;
        if (i >= s.Length) return ("", s);          // 공백 없음
        string first = s[..i];
        string rest = s[i..].TrimStart();
        if (first.Length > 0 && first.All(char.IsDigit))
            return (first, rest);
        return ("", s);
    }
}
