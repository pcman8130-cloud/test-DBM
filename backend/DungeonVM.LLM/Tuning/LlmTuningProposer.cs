namespace DungeonVM.LLM.Tuning;

/// <summary>ILlmClient(Claude/OpenAI)로 실제 변경안을 요청하는 제안자.</summary>
public sealed class LlmTuningProposer : ITuningProposer
{
    private readonly ILlmClient _client;

    public LlmTuningProposer(ILlmClient client) => _client = client;

    public string Name => $"LLM({_client.ProviderName})";

    public async Task<IReadOnlyList<ParameterChange>> ProposeAsync(TuningContext context, CancellationToken ct = default)
    {
        if (!_client.IsConfigured)
            throw new InvalidOperationException($"{_client.ProviderName} API 키가 설정되어 있지 않습니다.");

        string system = TuningPromptTemplate.BuildSystemPrompt(context.MaxChanges);
        string user = TuningPromptTemplate.BuildUserPrompt(context);
        string raw = await _client.CompleteJsonAsync(system, user, ct).ConfigureAwait(false);
        return TuningProposalParser.Parse(raw);
    }
}
