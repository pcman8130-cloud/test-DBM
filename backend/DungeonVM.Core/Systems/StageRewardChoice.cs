using DungeonVM.Core.Enums;
using DungeonVM.Core.Models;

namespace DungeonVM.Core.Systems;

/// <summary>
/// 상자 1개의 설명. 상자를 고르면(StageLoop.OpenRewardBox) 이 수치로 후보 3개를 굴려 미리 보여 주고, 그중 하나를 고른다(2중 선택).
/// 여기에는 그 굴림에 쓰이는, 스테이지/등급에 따라 이미 계산된 수치만 들어 있다(UI가 범위를 보여줄 때도 사용).
/// StatBox: AttackPercent(공격력 %, 0.1 = +10%) / HealthBonus(체력 고정값) / Dodge·AttackSpeed·CooldownReduction(유틸, 비율).
/// SpecialBox: RuneChance(룬 확률, 나머지는 유물).
/// EquipmentBox: WeaponMin/MaxTier(무기 티어 범위; WeaponLowestChance>0이면 그 확률로 최소 티어, 아니면 균등), ArmorEpic/LegendaryChance(방어구 등급 확률, 나머지는 Rare),
/// GoldAmount(후보 중 골드 선택지의 금액, 유물 골드 배율 적용 전).
/// </summary>
public sealed record StageRewardOption(
    StageRewardOptionType Type,
    double AttackPercent = 0,
    double HealthBonus = 0,
    double DodgeBonus = 0,
    double AttackSpeedBonus = 0,
    double CooldownReduction = 0,
    double RuneChance = 0,
    int WeaponMinTier = 0,
    int WeaponMaxTier = 0,
    double WeaponLowestChance = 0,
    double ArmorEpicChance = 0,
    double ArmorLegendaryChance = 0,
    int GoldAmount = 0);

/// <summary>
/// 상자를 열었을 때 미리 보여 주는 후보 1개(예: "지팡이 Lv.3", "방어구 Rare", "골드 80"). 종류별로 해당 필드만 채워진다:
/// Weapon/Armor → 실제로 받게 될 아이템(이미 굴려져 있음), Gold → GoldAmount, Rune → RuneElement, Relic → Relic,
/// 능력치 계열 → Amount(공격력%·공속·회피·쿨감은 비율, 체력은 고정값).
/// </summary>
public sealed record StageRewardCandidate(
    StageRewardOutcomeKind Kind,
    string Description,
    Weapon? Weapon = null,
    Armor? Armor = null,
    int GoldAmount = 0,
    ElementType? RuneElement = null,
    RelicDef? Relic = null,
    double Amount = 0);

/// <summary>상자를 열어 굴린 후보 목록(보통 3개). 플레이어/봇이 이 중 하나를 골라 StageLoop.ClaimRewardCandidate로 확정한다.</summary>
public sealed record StageRewardReveal(StageRewardOptionType Box, IReadOnlyList<StageRewardCandidate> Candidates);

/// <summary>후보 중 하나를 확정해 실제로 받은 결과. Stored=false는 장비를 받을 그리드 칸이 없어 버려졌다는 뜻이다.</summary>
public sealed record StageRewardOutcome(StageRewardOutcomeKind Kind, string Description, bool Stored = true);

/// <summary>
/// StageLoop.CompleteStageVictory가 반환하는, 이번 스테이지 클리어에서 고를 수 있는 3개 상자 목록
/// (장비 상자 / 특수 상자 / 능력치 상자). 상자를 고르면 StageLoop.OpenRewardBox가 후보 3개를 굴려 보여 주고,
/// 그중 하나를 StageLoop.ClaimRewardCandidate로 확정한다.
/// </summary>
public sealed record StageRewardChoice(StageTier Tier, IReadOnlyList<StageRewardOption> Options)
{
    /// <summary>주어진 유형의 옵션이 몇 번째 인덱스인지 찾는다. 봇이 "항상 능력치 상자" 같은 고정 정책을 쓸 때 편리하다.</summary>
    public int IndexOf(StageRewardOptionType type)
    {
        for (int i = 0; i < Options.Count; i++)
            if (Options[i].Type == type) return i;
        return 0;
    }
}
