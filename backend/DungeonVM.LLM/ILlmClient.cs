namespace DungeonVM.LLM;

/// <summary>JSON Mode로 완성 텍스트를 반환하는 LLM 제공자 추상화(OpenAI/Claude 등을 교체 가능하게).</summary>
public interface ILlmClient
{
    string ProviderName { get; }

    /// <summary>API 키 등 호출에 필요한 자격 증명이 환경에 설정되어 있는지 여부.</summary>
    bool IsConfigured { get; }

    Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}
