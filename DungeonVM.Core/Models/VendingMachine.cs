using DungeonVM.Core.Enums;

namespace DungeonVM.Core.Models;

/// <summary>체력만 보유한 방어 목표물이자 공격/방어 뽑기 UI의 실체. 업그레이드할수록 고티어/고등급 확률이 오른다.</summary>
public sealed class VendingMachine
{
    private const double BaseHealth = 300;
    public const int MaxUpgradeLevel = 5;

    public double MaxHealth { get; private set; } = BaseHealth;
    public double CurrentHealth { get; private set; }
    public int AttackUpgradeLevel { get; private set; } = 1;
    public int DefenseUpgradeLevel { get; private set; } = 1;

    private static readonly WeaponType[] AllWeaponTypes = (WeaponType[])Enum.GetValues(typeof(WeaponType));
    private static readonly ArmorType[] AllArmorTypes = (ArmorType[])Enum.GetValues(typeof(ArmorType));

    public VendingMachine()
    {
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
        double tier2Chance = 0.05 + (AttackUpgradeLevel - 1) * 0.08; // Lv1: 5% ~ Lv5: 37%

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
        double legendary = 0.01 + (DefenseUpgradeLevel - 1) * 0.02;
        double epic = 0.08 + (DefenseUpgradeLevel - 1) * 0.05;
        double rare = 0.30 + (DefenseUpgradeLevel - 1) * 0.05;

        var rarity = roll switch
        {
            _ when roll < legendary => ArmorRarity.Legendary,
            _ when roll < legendary + epic => ArmorRarity.Epic,
            _ when roll < legendary + epic + rare => ArmorRarity.Rare,
            _ => ArmorRarity.Common,
        };

        return Armor.RollRandom(rng, type, rarity);
    }

    public static int UpgradeGoldCost(int currentLevel) => currentLevel switch
    {
        1 => 150,
        2 => 350,
        3 => 700,
        4 => 1300,
        _ => int.MaxValue,
    };

    public int AttackUpgradeCost => UpgradeGoldCost(AttackUpgradeLevel);
    public int DefenseUpgradeCost => UpgradeGoldCost(DefenseUpgradeLevel);
    public bool CanUpgradeAttack => AttackUpgradeLevel < MaxUpgradeLevel;
    public bool CanUpgradeDefense => DefenseUpgradeLevel < MaxUpgradeLevel;

    /// <summary>골드 차감은 호출자(CurrencyManager)가 책임지고, 성공 시에만 레벨을 올린다.</summary>
    public void UpgradeAttackLevel() => AttackUpgradeLevel = Math.Min(MaxUpgradeLevel, AttackUpgradeLevel + 1);
    public void UpgradeDefenseLevel() => DefenseUpgradeLevel = Math.Min(MaxUpgradeLevel, DefenseUpgradeLevel + 1);

    public const int WeaponRollCost = 20;
    public const int ArmorRollCost = 20;
}
