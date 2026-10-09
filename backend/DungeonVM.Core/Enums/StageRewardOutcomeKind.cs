namespace DungeonVM.Core.Enums;

/// <summary>상자를 열었을 때 실제로 나온 결과의 종류(UI 연출/로그/시뮬레이터 집계용).</summary>
public enum StageRewardOutcomeKind
{
    Weapon,
    Gold,
    Armor,
    Rune,
    Relic,
    AttackPercent,
    Health,
    Dodge,
    AttackSpeed,
    CooldownReduction,
}
