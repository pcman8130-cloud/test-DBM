using DungeonVM.Core.Enums;

namespace DungeonVM.Core.Systems;

/// <summary>
/// 선택보상 1개의 내용. Type에 따라 나머지 필드 중 일부만 의미를 가진다.
/// Gold: GoldAmount. StatBoost: AttackBonus/HealthBonus(파티 전원에게 영구 가산). Box: RuneChance(나머지 확률은 유물).
/// </summary>
public sealed record StageRewardOption(
    StageRewardOptionType Type,
    int GoldAmount = 0,
    double AttackBonus = 0,
    double HealthBonus = 0,
    double RuneChance = 0);

/// <summary>
/// StageLoop.CompleteStageVictory가 반환하는, 이번 스테이지 클리어에서 고를 수 있는 3개 선택보상 목록.
/// 실제 선택(봇 정책 또는 Unity UI)은 StageLoop.ResolveStageRewardChoice에서 인덱스로 확정한다.
/// </summary>
public sealed record StageRewardChoice(StageTier Tier, IReadOnlyList<StageRewardOption> Options)
{
    /// <summary>주어진 유형의 옵션이 몇 번째 인덱스인지 찾는다. 봇이 "항상 골드" 같은 고정 정책을 쓸 때 편리하다.</summary>
    public int IndexOf(StageRewardOptionType type)
    {
        for (int i = 0; i < Options.Count; i++)
            if (Options[i].Type == type) return i;
        return 0;
    }
}
