namespace DungeonVM.LLM;

/// <summary>환경 변수에 키가 설정된 첫 번째 제공자를 고른다(우선순위: Anthropic → OpenAI → Gemini).</summary>
public static class LlmClientFactory
{
    public static ILlmClient? CreateConfigured()
    {
        foreach (ILlmClient client in new ILlmClient[] { new AnthropicClient(), new OpenAiClient(), new GeminiClient() })
            if (client.IsConfigured) return client;
        return null;
    }
}
