namespace Saenggibu;

/// <summary>
/// app/engine.py 의 _build_messages 이식 — 생성 입력(채팅 메시지) 구성.
/// 모델 로드/추론은 LLamaSharp 쪽(Cli.Infer). 여기선 결정적 메시지 조립만.
/// </summary>
public static class Engine
{
    public sealed record Message(string Role, string Content);

    public static List<Message> BuildMessages(AreaSpec area, MemoryStore store,
        string subject, string keywords, string tone = "", string lengthHint = "",
        int nVariations = 1)
    {
        var messages = new List<Message> { new("system", area.SystemPrompt()) };

        string query = $"{subject} {keywords}";
        var teacher = store.Retrieve(area.Key, query, Config.FewshotK, subject);
        int need = Math.Max(0, Config.FewshotK - teacher.Count) + Config.SeedFewshotK;
        var seed = store.RetrieveSeed(area.Key, query, need, subject);

        // fewshot = seed + teacher (교사 예시를 뒤에 = 더 가깝게)
        var fewshot = new List<MemoryStore.Example>(seed);
        fewshot.AddRange(teacher);

        if (fewshot.Count == 0 && area.ColdStart.output.Length > 0)
        {
            var (csSubject, csKeywords, csOutput) = area.ColdStart;
            fewshot = new List<MemoryStore.Example>
            {
                new(-1, area.Key, csSubject, csKeywords, csOutput, 1, 0.0),
            };
        }

        foreach (var ex in fewshot)
        {
            messages.Add(new("user", Prompts.BuildUserPrompt(area, ex.Subject, ex.Keywords, "", "")));
            messages.Add(new("assistant", ex.OutputText));
        }

        if (fewshot.Count > 0)
        {
            string note = "아래 대화에는 생기부 문장의 모범 작성 예시가 포함되어 있다. " +
                          "예시의 '내용'이 아니라 '문장 형식·어투·연결·종결 방식'을 그대로 따라, " +
                          "지금 교사가 준 키워드로 자연스러운 생기부 문장을 작성하라.";
            if (teacher.Count > 0)
                note += " 특히 교사가 직접 채택한 예시의 문체를 최우선으로 따르라.";
            messages.Insert(1, new("system", note));
        }

        messages.Add(new("user", Prompts.BuildUserPrompt(area, subject, keywords, tone, lengthHint, nVariations)));
        return messages;
    }
}
