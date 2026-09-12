using DungeonVM.Core.Balance;
using DungeonVM.Core.Enums;

namespace DungeonVM.Core.Models;

/// <summary>무기 종류별 기초 스탯(1티어 기준) 및 사거리/진형 배치 규칙. 실제 수치는 BalanceProvider(엑셀→JSON 파이프라인 산출물)에서 온다.</summary>
public static class WeaponCatalog
{
    public static int MaxTier => BalanceProvider.Current.Weapons.MaxTier;

    public sealed record WeaponBase(
        WeaponType Type,
        RowPosition Row,
        double BaseDamage,
        double AttacksPerSecond,
        double BonusHealth,
        double PullAggro); // 방패의 어그로/밀치기, 성서의 아군 치유량 등 특수 기믹 강도로 재사용

    private static BalanceData? _cachedSource;
    private static Dictionary<WeaponType, WeaponBase>? _cachedTable;

    private static Dictionary<WeaponType, WeaponBase> Table
    {
        get
        {
            var current = BalanceProvider.Current;
            if (!ReferenceEquals(_cachedSource, current))
            {
                _cachedTable = BuildTable(current);
                _cachedSource = current;
            }
            return _cachedTable!;
        }
    }

    private static Dictionary<WeaponType, WeaponBase> BuildTable(BalanceData data)
    {
        var table = new Dictionary<WeaponType, WeaponBase>();
        foreach (var (key, entry) in data.Weapons.Table)
        {
            var type = Enum.Parse<WeaponType>(key);
            var row = Enum.Parse<RowPosition>(entry.Row);
            table[type] = new WeaponBase(type, row, entry.BaseDamage, entry.AttacksPerSecond, entry.BonusHealth, entry.PullAggro);
        }
        return table;
    }

    public static WeaponBase Get(WeaponType type) => Table[type];

    /// <summary>레벨 1~MaxTier 성장 배율(1레벨=1.0). 인덱스 범위를 벗어나면 마지막 값으로 clamp한다.</summary>
    public static double LevelMultiplierAt(int tier)
    {
        var list = BalanceProvider.Current.Weapons.LevelMultipliers;
        if (list.Count == 0) return 1.0;
        int index = Math.Clamp(tier - 1, 0, list.Count - 1);
        return list[index];
    }

    /// <summary>레벨 5/10/15(무기 스킬 해금) 도달 시 가산되는 전투력 보너스. 근사 스킬 반영이며 스택 누적된다.</summary>
    public static double SkillBonusAt(int tier)
    {
        var w = BalanceProvider.Current.Weapons;
        double bonus = 0;
        if (tier >= 5) bonus += w.SkillBonusAtLevel5;
        if (tier >= 10) bonus += w.SkillBonusAtLevel10;
        if (tier >= 15) bonus += w.SkillBonusAtLevel15;
        return bonus;
    }

    public static double DamageAtTier(WeaponType type, int tier)
        => Table[type].BaseDamage * LevelMultiplierAt(tier) * (1 + SkillBonusAt(tier));

    /// <summary>성서(Bible) 전용: PullAggro 필드를 초당 치유량 기초값으로 재사용해 파티 전체를 회복시킨다.</summary>
    public static double HealPerSecondAtTier(WeaponType type, int tier)
        => Table[type].PullAggro * LevelMultiplierAt(tier) * (1 + SkillBonusAt(tier));

    public static RowPosition RowOf(WeaponType type) => Table[type].Row;
}
