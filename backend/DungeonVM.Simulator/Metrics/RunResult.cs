using DungeonVM.Core.Enums;

namespace DungeonVM.Simulator.Metrics;

/// <summary>
/// FinalWeapons/FinalArmors는 "Sword_T2", "Robe_Epic" 형태의 문자열로 직렬화한다.
/// ValueTuple은 System.Text.Json 기본 옵션으로는 직렬화되지 않으므로 로그 싱크(파일/Supabase) 호환을 위해 문자열로 고정.
/// </summary>
public sealed record RunResult(
    string BotName,
    int RunIndex,
    RunEndReason Outcome,
    int StagesCleared,
    int FinalGold,
    int FinalGems,
    int FinalSouls,
    IReadOnlyList<string> FinalWeapons,
    IReadOnlyList<string> FinalArmors
);
