using DungeonVM.Core.Balance;
using DungeonVM.Core.Enums;

namespace DungeonVM.Core.Models;

/// <summary>공격/방어 뽑기 UI의 실체. 체력이 없어 공격받지 않으며, 업그레이드할수록 고티어/고등급 확률이 오른다.
/// 패배 조건은 오직 출격한 모험가 전원 전멸뿐이다.</summary>
public sealed class VendingMachine
{
    private static VendingMachineBalanceSection Config => BalanceProvider.Current.VendingMachine;

    public static int MaxUpgradeLevel => Config.MaxUpgradeLevel;

    public int AttackUpgradeLevel { get; private set; } = 1;
    public int DefenseUpgradeLevel { get; private set; } = 1;

    private static readonly WeaponType[] AllWeaponTypes = (WeaponType[])Enum.GetValues(typeof(WeaponType));
    private static readonly ArmorType[] AllArmorTypes = (ArmorType[])Enum.GetValues(typeof(ArmorType));

    /// <summary>더 이상 자판기에서 나오지 않게 막은 무기 종류(고대 주화 유물).</summary>
    public HashSet<WeaponType> BannedWeaponTypes { get; } = new();

    /// <summary>속임수 동전 유물: 다음 뽑기(무기/방어구)가 무료인지.</summary>
    public bool NextRollFree { get; set; }

    /// <summary>MetaProgression.FirstRollTierBoostChance 판정 성공 시 다음 1회 뽑기를 최소 2티어로 보장한다.</summary>
    public bool NextRollGuaranteedTier2 { get; set; }

    /// <summary>자판기 강화 레벨(1~5)별 뽑기 등장 티어 확률표. 프로토타입(index.html)의 WEAPON_LEVEL_TABLE과
    /// 정확히 동일하다 — 예전엔 이 표 없이 "레벨이 오를수록 2티어 확률만 선형 증가"하는 이진(1티어 vs 2티어)
    /// 방식이었는데, 그러면 자판기를 아무리 강화해도 뽑기로는 절대 2티어를 못 넘고 나머지는 전부 머지로만
    /// 채워야 해서 같은 골드 투자 대비 무기 성장 속도가 프로토타입보다 크게 느렸다(스테15 보스전 화력 부족의
    /// 실질적 원인). 프로토타입은 Lv.5 자판기에서 곧바로 3~7티어 무기가 나온다.</summary>
    private static readonly (int Tier, double Weight)[][] WeaponRollTierTable =
    {
        new (int, double)[] { (1, 100) },
        new (int, double)[] { (1, 78), (2, 19), (3, 3) },
        new (int, double)[] { (1, 65), (2, 22), (3, 10), (4, 3) },
        new (int, double)[] { (2, 55), (3, 28), (4, 13), (5, 4) },
        new (int, double)[] { (3, 50), (4, 33), (5, 15.5), (6, 1), (7, 0.5) },
    };

    public Weapon RollWeapon(Random rng)
    {
        var allowed = AllWeaponTypes.Where(t => !BannedWeaponTypes.Contains(t)).ToArray();
        var type = allowed[rng.Next(allowed.Length)];
        int tier = RollWeaponTier(rng);

        if (NextRollGuaranteedTier2)
        {
            tier = Math.Max(tier, 2);
            NextRollGuaranteedTier2 = false;
        }

        return new Weapon(type, tier);
    }

    private int RollWeaponTier(Random rng)
    {
        var table = WeaponRollTierTable[Math.Clamp(AttackUpgradeLevel - 1, 0, WeaponRollTierTable.Length - 1)];
        double roll = rng.NextDouble() * 100;
        double cumulative = 0;
        foreach (var (tier, weight) in table)
        {
            cumulative += weight;
            if (roll < cumulative) return tier;
        }
        return table[^1].Tier;
    }

    public Armor RollArmor(Random rng)
    {
        var type = AllArmorTypes[rng.Next(AllArmorTypes.Length)];
        double roll = rng.NextDouble();
        double legendary = Config.LegendaryChanceBase + (DefenseUpgradeLevel - 1) * Config.LegendaryChancePerLevel;
        double epic = Config.EpicChanceBase + (DefenseUpgradeLevel - 1) * Config.EpicChancePerLevel;
        double rare = Config.RareChanceBase + (DefenseUpgradeLevel - 1) * Config.RareChancePerLevel;

        var rarity = roll switch
        {
            _ when roll < legendary => ArmorRarity.Legendary,
            _ when roll < legendary + epic => ArmorRarity.Epic,
            _ when roll < legendary + epic + rare => ArmorRarity.Rare,
            _ => ArmorRarity.Common,
        };

        return Armor.RollRandom(rng, type, rarity);
    }

    public static int UpgradeGoldCost(int currentLevel)
    {
        var costs = Config.UpgradeGoldCosts;
        return currentLevel >= 1 && currentLevel <= costs.Count ? costs[currentLevel - 1] : int.MaxValue;
    }

    public int AttackUpgradeCost => UpgradeGoldCost(AttackUpgradeLevel);
    public int DefenseUpgradeCost => UpgradeGoldCost(DefenseUpgradeLevel);
    public bool CanUpgradeAttack => AttackUpgradeLevel < MaxUpgradeLevel;
    public bool CanUpgradeDefense => DefenseUpgradeLevel < MaxUpgradeLevel;

    /// <summary>골드 차감은 호출자(CurrencyManager)가 책임지고, 성공 시에만 레벨을 올린다.</summary>
    public void UpgradeAttackLevel() => AttackUpgradeLevel = Math.Min(MaxUpgradeLevel, AttackUpgradeLevel + 1);
    public void UpgradeDefenseLevel() => DefenseUpgradeLevel = Math.Min(MaxUpgradeLevel, DefenseUpgradeLevel + 1);

    public static int WeaponRollCost => Config.WeaponRollCost;
    public static int ArmorRollCost => Config.ArmorRollCost;
}
