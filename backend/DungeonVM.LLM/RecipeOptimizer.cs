using System.Text.Json;
using DungeonVM.LLM.PromptTemplates;

namespace DungeonVM.LLM;

/// <summary>
/// 시뮬레이터가 집계한 아이템 채택률에서 5% 미만 저채택 아이템을 감지하면
/// OpenAI/Claude API(JSON Mode)를 호출해 레시피/스탯 재조정안을 제안받는 오프라인 밸런싱 모듈.
/// API 키가 없는 환경(CI, 로컬 무키 실행)에서는 네트워크 호출 없이 조용히 스킵한다.
/// </summary>
public sealed class RecipeOptimizer
{
    public const double AdoptionThreshold = 0.05;

    private readonly ILlmClient _client;

    public event Action<string>? OnLog;

    public RecipeOptimizer(ILlmClient client)
    {
        _client = client;
    }

    public static List<LowAdoptionItem> DetectLowAdoption(IEnumerable<LowAdoptionItem> allItems)
        => allItems.Where(i => i.AdoptionRate < AdoptionThreshold).ToList();

    public async Task<IReadOnlyList<BalanceSuggestion>> OptimizeAsync(
        IReadOnlyList<LowAdoptionItem> lowAdoptionItems, CancellationToken ct = default)
    {
        if (lowAdoptionItems.Count == 0)
            return Array.Empty<BalanceSuggestion>();

        if (!_client.IsConfigured)
        {
            OnLog?.Invoke(
                $"[{_client.ProviderName}] API 키가 설정되지 않아 오프라인 모드로 건너뜁니다. " +
                $"(저채택 아이템 {lowAdoptionItems.Count}건 감지됨: {string.Join(", ", lowAdoptionItems.Select(i => i.ItemId))})");
            return Array.Empty<BalanceSuggestion>();
        }

        OnLog?.Invoke($"[{_client.ProviderName}] 저채택 아이템 {lowAdoptionItems.Count}건에 대한 밸런싱 제안을 요청합니다.");

        string userPrompt = BalancePromptTemplate.BuildUserPrompt(lowAdoptionItems);
        string raw = await _client.CompleteJsonAsync(BalancePromptTemplate.SystemPrompt, userPrompt, ct)
            .ConfigureAwait(false);

        var suggestions = ParseSuggestions(raw);
        OnLog?.Invoke($"[{_client.ProviderName}] 제안 {suggestions.Count}건 수신 완료.");
        return suggestions;
    }

    public static async Task SaveSuggestionsAsync(string path, IReadOnlyList<BalanceSuggestion> suggestions, CancellationToken ct = default)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(suggestions, options), ct).ConfigureAwait(false);
    }

    internal static IReadOnlyList<BalanceSuggestion> ParseSuggestions(string raw)
    {
        try
        {
            string json = ExtractJson(raw);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            JsonElement array = root.ValueKind == JsonValueKind.Array
                ? root
                : root.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array
                    ? itemsEl
                    : default;

            if (array.ValueKind != JsonValueKind.Array)
                return Array.Empty<BalanceSuggestion>();

            var results = new List<BalanceSuggestion>();
            foreach (var el in array.EnumerateArray())
            {
                string itemId = el.TryGetProperty("itemId", out var idEl) ? idEl.GetString() ?? "" : "";
                string change = el.TryGetProperty("suggestedChange", out var chEl) ? chEl.GetString() ?? "" : "";
                string reasoning = el.TryGetProperty("reasoning", out var rEl) ? rEl.GetString() ?? "" : "";
                if (itemId.Length > 0)
                    results.Add(new BalanceSuggestion(itemId, change, reasoning));
            }
            return results;
        }
        catch (JsonException)
        {
            return Array.Empty<BalanceSuggestion>();
        }
    }

    /// <summary>모델이 코드펜스(```json ... ```)로 감싸 응답하는 경우를 대비한 방어적 파싱.</summary>
    private static string ExtractJson(string raw)
    {
        raw = raw.Trim();
        if (!raw.StartsWith("```")) return raw;

        int firstNewline = raw.IndexOf('\n');
        int lastFence = raw.LastIndexOf("```", StringComparison.Ordinal);
        if (firstNewline < 0 || lastFence <= firstNewline) return raw;

        return raw[(firstNewline + 1)..lastFence].Trim();
    }
}
