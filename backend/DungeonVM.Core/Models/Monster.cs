using DungeonVM.Core.Enums;

namespace DungeonVM.Core.Models;

public sealed class Monster
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; }
    public ElementType Element { get; }
    public double MaxHealth { get; }
    public double CurrentHealth { get; private set; }
    public double Damage { get; }
    public double AttacksPerSecond { get; }
    public int GoldReward { get; }
    public bool IsMidBoss { get; }
    public bool IsBigBoss { get; }

    public Monster(string name, ElementType element, double maxHealth, double damage, double attacksPerSecond,
        int goldReward, bool isMidBoss = false, bool isBigBoss = false)
    {
        Name = name;
        Element = element;
        MaxHealth = maxHealth;
        CurrentHealth = maxHealth;
        Damage = damage;
        AttacksPerSecond = attacksPerSecond;
        GoldReward = goldReward;
        IsMidBoss = isMidBoss;
        IsBigBoss = isBigBoss;
    }

    public bool IsAlive => CurrentHealth > 0;

    public void TakeDamage(double amount)
    {
        CurrentHealth = Math.Max(0, CurrentHealth - amount);
    }
}
