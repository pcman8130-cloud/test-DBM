using DungeonVM.Core.Enums;

namespace DungeonVM.Core.Models;

/// <summary>무기 종류별 기초 스탯(1티어 기준) 및 사거리/진형 배치 규칙. 밸런스 조정은 이 테이블만 손대면 된다.</summary>
public static class WeaponCatalog
{
    public const int MaxTier = 5;

    /// <summary>머지 1회(동일 무기 2개 결합)당 데미지 성장 배율.</summary>
    public const double TierDamageMultiplier = 1.6;

    public sealed record WeaponBase(
        WeaponType Type,
        RowPosition Row,
        double BaseDamage,
        double AttacksPerSecond,
        double BonusHealth,
        double PullAggro); // 방패의 어그로/밀치기, 성서의 아군 치유량 등 특수 기믹 강도로 재사용

    private static readonly Dictionary<WeaponType, WeaponBase> Table = new()
    {
        // 근접/전열: 칼(밸런스), 방패(고체력/어그로), 단검(고속 저데미지)
        [WeaponType.Sword] = new WeaponBase(WeaponType.Sword, RowPosition.Front, BaseDamage: 12, AttacksPerSecond: 1.2, BonusHealth: 20, PullAggro: 0),
        [WeaponType.Shield] = new WeaponBase(WeaponType.Shield, RowPosition.Front, BaseDamage: 5, AttacksPerSecond: 0.8, BonusHealth: 60, PullAggro: 1.0),
        [WeaponType.Dagger] = new WeaponBase(WeaponType.Dagger, RowPosition.Front, BaseDamage: 7, AttacksPerSecond: 2.2, BonusHealth: 10, PullAggro: 0),

        // 원거리/후열: 활(밸런스), 지팡이(광역), 성서(아군 치유)
        [WeaponType.Bow] = new WeaponBase(WeaponType.Bow, RowPosition.Back, BaseDamage: 10, AttacksPerSecond: 1.4, BonusHealth: 8, PullAggro: 0),
        [WeaponType.Staff] = new WeaponBase(WeaponType.Staff, RowPosition.Back, BaseDamage: 9, AttacksPerSecond: 0.9, BonusHealth: 8, PullAggro: 0),
        [WeaponType.Bible] = new WeaponBase(WeaponType.Bible, RowPosition.Back, BaseDamage: 4, AttacksPerSecond: 1.0, BonusHealth: 8, PullAggro: 0), // PullAggro 필드를 초당 치유량으로 재사용
    };

    public static WeaponBase Get(WeaponType type) => Table[type];

    public static double DamageAtTier(WeaponType type, int tier)
        => Table[type].BaseDamage * Math.Pow(TierDamageMultiplier, Math.Max(0, tier - 1));

    public static RowPosition RowOf(WeaponType type) => Table[type].Row;
}
