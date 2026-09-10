namespace DungeonVM.LLM;

/// <summary>시뮬레이터 집계 결과에서 채택률이 임계값 미만으로 감지된 아이템 1건.</summary>
public sealed record LowAdoptionItem(
    string ItemId,           // 예: "Weapon.Dagger.T3", "Armor.Robe.Epic"
    string Category,         // "Weapon" | "Armor"
    double AdoptionRate,     // 0.0 ~ 1.0
    int SampleSize,          // 관측된 총 런/장착 표본 수
    string CurrentStatsJson  // 현재 스탯을 그대로 직렬화한 JSON (LLM 컨텍스트용)
);
