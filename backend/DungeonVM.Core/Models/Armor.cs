using DungeonVM.Core.Balance;
using DungeonVM.Core.Enums;

namespace DungeonVM.Core.Models;

/// <summary>완제품으로 드롭/구매되는 방어구. 머지 없이 등급(Rarity)에 따라 랜덤 스탯 폭만 달라진다.</summary>
public sealed class Armor
{
    public Guid Id { get; } = Guid.NewGuid();
    public ArmorType Type { get; }
    public ArmorRarity Rarity { get; }
    public double BonusHealth { get; }
    public double AttackSpeedBonus { get; }
    public double DodgeChance { get; }

    public Armor(ArmorType type, ArmorRarity rarity, double bonusHealth, double attackSpeedBonus, double dodgeChance)
    {
        Type = type;
        Rarity = rarity;
        BonusHealth = bonusHealth;
        AttackSpeedBonus = attackSpeedBonus;
        DodgeChance = Math.Clamp(dodgeChance, 0, BalanceProvider.Current.Armor.DodgeClampMax);
    }

    public static Armor RollRandom(Random rng, ArmorType type, ArmorRarity rarity)
    {
        var range = BalanceProvider.Current.Armor.RollRanges[rarity.ToString()];
        double hp = Lerp(rng, range.MinHp, range.MaxHp);
        double atk = Lerp(rng, range.MinAtk, range.MaxAtk);
        double dodge = Lerp(rng, range.MinDodge, range.MaxDodge);
        return new Armor(type, rarity, hp, atk, dodge);
    }

    private static double Lerp(Random rng, double min, double max) => min + rng.NextDouble() * (max - min);
}
