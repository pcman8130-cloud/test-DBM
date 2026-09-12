using DungeonVM.Core.Balance;
using DungeonVM.Core.Enums;

namespace DungeonVM.Core.Models;

/// <summary>무기 종류별 기초 스탯(1티어 기준) 및 사거리/진형 배치 규칙. 실제 수치는 BalanceProvider(엑셀→JSON 파이프라인 산출물)에서 온다.</summary>
public static class WeaponCatalog
{
    public static int MaxTier => BalanceProvider.Current.Weapons.MaxTier;

    /// <summary>머지 1회(동일 무기 2개 결합)당 데미지 성장 배율.</summary>
    public static double TierDamageMultiplier => BalanceProvider.Current.Weapons.TierDamageMultiplier;

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

    public static double DamageAtTier(WeaponType type, int tier)
        => Table[type].BaseDamage * Math.Pow(TierDamageMultiplier, Math.Max(0, tier - 1));

    public static RowPosition RowOf(WeaponType type) => Table[type].Row;
}
