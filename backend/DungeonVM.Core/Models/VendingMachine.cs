using DungeonVM.Core.Balance;
using DungeonVM.Core.Enums;

namespace DungeonVM.Core.Models;

/// <summary>체력만 보유한 방어 목표물이자 공격/방어 뽑기 UI의 실체. 업그레이드할수록 고티어/고등급 확률이 오른다.</summary>
public sealed class VendingMachine
{
    private static VendingMachineBalanceSection Config => BalanceProvider.Current.VendingMachine;

    public static int MaxUpgradeLevel => Config.MaxUpgradeLevel;

    public double MaxHealth { get; private set; }
    public double CurrentHealth { get; private set; }
    public int AttackUpgradeLevel { get; private set; } = 1;
    public int DefenseUpgradeLevel { get; private set; } = 1;

    private static readonly WeaponType[] AllWeaponTypes = (WeaponType[])Enum.GetValues(typeof(WeaponType));
    private static readonly ArmorType[] AllArmorTypes = (ArmorType[])Enum.GetValues(typeof(ArmorType));

    public VendingMachine()
    {
        MaxHealth = Config.BaseHealth;
        CurrentHealth = MaxHealth;
    }

    public bool IsDestroyed => CurrentHealth <= 0;

    public void TakeDamage(double amount) => CurrentHealth = Math.Max(0, CurrentHealth - amount);

    /// <summary>MetaProgression.FirstRollTierBoostChance 판정 성공 시 다음 1회 뽑기를 2티어로 확정한다.</summary>
    public bool NextRollGuaranteedTier2 { get; set; }

    /// <summary>업그레이드 레벨이 오를수록 2티어 무기 등장 확률이 선형으로 증가한다(고티어는 뽑기가 아닌 머지로만 도달).</summary>
    public Weapon RollWeapon(Random rng)
    {
        var type = AllWeaponTypes[rng.Next(AllWeaponTypes.Length)];
        double tier2Chance = Config.Tier2ChanceBase + (AttackUpgradeLevel - 1) * Config.Tier2ChancePerLevel;

        if (NextRollGuaranteedTier2)
        {
            tier2Chance = 1.0;
            NextRollGuaranteedTier2 = false;
        }

        int tier = rng.NextDouble() < tier2Chance ? 2 : 1;
        return new Weapon(type, tier);
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
