namespace DungeonVM.LLM;

/// <summary>LLM이 JSON Mode로 반환한 단일 아이템에 대한 밸런스 조정 제안.</summary>
public sealed record BalanceSuggestion(
    string ItemId,
    string SuggestedChange,   // 예: "BaseDamage +18%, AttacksPerSecond +0.1"
    string Reasoning
);
