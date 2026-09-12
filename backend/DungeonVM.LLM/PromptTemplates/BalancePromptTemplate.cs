using System.Text;
using System.Text.Json;

namespace DungeonVM.LLM.PromptTemplates;

/// <summary>저채택 아이템 레시피/스탯 재조정을 요청하는 프롬프트 템플릿.</summary>
public static class BalancePromptTemplate
{
    public const string SystemPrompt =
        "당신은 캐주얼 머지 오토배틀러 로그라이트 디펜스 게임 '던전 자판기'의 밸런스 디자이너입니다. " +
        "채택률(장착/보유 비율)이 5% 미만으로 떨어진 아이템의 레시피 또는 스탯을 조정할 것을 제안하세요. " +
        "반드시 JSON 배열만 출력하고, 각 원소는 {\"itemId\": string, \"suggestedChange\": string, \"reasoning\": string} 형식이어야 합니다. " +
        "다른 설명 문장을 JSON 밖에 추가하지 마세요.";

    public static string BuildUserPrompt(IReadOnlyList<LowAdoptionItem> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("다음은 3,000회 헤드리스 배치 시뮬레이션에서 채택률 5% 미만으로 감지된 아이템 목록입니다:");
        sb.AppendLine();

        foreach (var item in items)
        {
            sb.AppendLine($"- itemId: {item.ItemId}");
            sb.AppendLine($"  category: {item.Category}");
            sb.AppendLine($"  adoptionRate: {item.AdoptionRate:P2} (sampleSize={item.SampleSize})");
            sb.AppendLine($"  currentStats: {item.CurrentStatsJson}");
        }

        sb.AppendLine();
        sb.AppendLine("각 아이템에 대해 채택률을 끌어올릴 수 있는 구체적인 수치 조정안을 JSON 배열로 제안하세요.");
        return sb.ToString();
    }

    public static string BuildRequestJson(IReadOnlyList<LowAdoptionItem> items)
    {
        return JsonSerializer.Serialize(new
        {
            system = SystemPrompt,
            user = BuildUserPrompt(items),
        });
    }
}
