using System.Text;

namespace Saenggibu;

/// <summary>app/prompts.py 의 AreaSpec 이식. 데이터는 PromptsData(생성됨), 로직은 여기.</summary>
public sealed class AreaSpec
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required bool SubjectField { get; init; }
    public required int CharLimit { get; init; }
    public required string InputHint { get; init; }
    public required string SystemExtra { get; init; }
    public (string subject, string keywords, string output) ColdStart { get; init; } = ("", "", "");

    public string SystemPrompt() =>
        "너는 대한민국 초·중·고 교사의 학교생활기록부 작성을 돕는 전문 조력자다. " +
        $"지금은 '{Title}' 항목을 작성한다.\n\n" +
        $"{PromptsData.CommonRules}\n\n{SystemExtra}\n\n" +
        "교사가 제공한 키워드·관찰 내용을 바탕으로, 위 원칙을 지켜 " +
        "바로 생기부에 입력 가능한 완성된 문장을 작성한다. " +
        "설명·머리말·따옴표 없이 본문만 출력한다.";
}

public static class Prompts
{
    public static List<AreaSpec> Areas => PromptsData.Areas;

    public static AreaSpec? ByKey(string key) => Areas.FirstOrDefault(a => a.Key == key);

    // build_user_prompt 의 변형모드 지시문(app/prompts.py 와 동일)
    private const string VariationBlock =
        "\n위 키워드의 핵심 내용을 항목별로 정리한다. 규칙:\n" +
        "- 한 줄에 하나의 핵심 내용을 쓴다.\n" +
        "- 각 핵심 내용은 그것을 표현하는 자연스러운 명사형 문장 5가지를, 의미는 같되 " +
        "어휘·표현·길이를 다르게 하여 ' / ' 로 구분해 나열한다.\n" +
        "- 각 표현은 한 문장으로 쓰고, 접속어로 시작하지 않는다.\n" +
        "- 주어진 키워드의 사실만 쓰고, 새로운 정보를 지어내지 않는다.\n" +
        "예시 형식:\n" +
        "맡은 역할을 성실히 수행함 / 맡은 일을 끝까지 책임감 있게 해냄 / 주어진 역할을 꾸준히 완수함 / " +
        "자신이 맡은 일에 최선을 다하는 모습을 보임 / 책임을 끝까지 다하는 자세를 지님\n" +
        "친구들을 배려하는 태도를 보임 / 또래를 따뜻하게 챙기는 모습을 드러냄 / 친구를 먼저 돕는 자세를 지님 / " +
        "어려운 친구를 살피는 마음을 지님 / 서로 돕는 분위기를 이끄는 모습을 보임";

    public static string BuildUserPrompt(AreaSpec area, string subject, string keywords,
                                         string tone, string lengthHint, int nVariations = 1)
    {
        var parts = new List<string>();
        if (area.SubjectField && subject.Trim().Length > 0)
            parts.Add($"과목: {subject.Trim()}");
        if (tone.Trim().Length > 0)
            parts.Add($"문체/톤 요청: {tone.Trim()}");
        if (lengthHint.Trim().Length > 0)
            parts.Add($"분량: {lengthHint.Trim()}");

        string label = area.Key == "polish" ? "다듬을 초안" : "키워드/관찰 내용";
        parts.Add($"{label}:\n{keywords.Trim()}");
        parts.Add("위 키워드에 나온 용어·고유명사(과목 전문 용어 포함)는 " +
                  "철자를 바꾸지 말고 그대로 정확히 사용한다.");

        if (nVariations > 1)
            parts.Add(VariationBlock);

        return string.Join("\n", parts);
    }
}
