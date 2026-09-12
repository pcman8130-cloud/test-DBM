using DungeonVM.Core.Balance;
using DungeonVM.Core.Enums;

namespace DungeonVM.Core.Models;

/// <summary>기본 스탯이 없는 빈 껍데기 아바타. 장착한 무기+방어구가 모든 전투 능력을 결정한다.</summary>
public sealed class Character
{
    private static CharacterBalanceSection Config => BalanceProvider.Current.Character;

    private readonly double _metaBaseHealthBonus;

    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; }
    public Weapon? EquippedWeapon { get; private set; }
    public Armor? EquippedArmor { get; private set; }
    public double CurrentHealth { get; private set; }
    public bool IsRetired { get; private set; }
    public double RetireRemainingSeconds { get; private set; }

    /// <summary>metaBaseHealthBonus: MetaProgression.BaseHealthBonus(영혼 스킬트리 효과)를 그대로 전달.</summary>
    public Character(string name, double metaBaseHealthBonus = 0)
    {
        Name = name;
        _metaBaseHealthBonus = metaBaseHealthBonus;
        CurrentHealth = MaxHealth;
    }

    public double MaxHealth => Config.BaseHealth + _metaBaseHealthBonus + (EquippedWeapon?.BonusHealth ?? 0) + (EquippedArmor?.BonusHealth ?? 0);
    public double AttackDamage => EquippedWeapon?.Damage ?? 0;
    public double AttacksPerSecond => (EquippedWeapon?.AttacksPerSecond ?? 0) * (1 + (EquippedArmor?.AttackSpeedBonus ?? 0));
    public double DodgeChance => EquippedArmor?.DodgeChance ?? 0;
    public RowPosition Row => EquippedWeapon is null ? RowPosition.Front : EquippedWeapon.Row;
    public ElementType Element => EquippedWeapon?.Element ?? ElementType.None;
    public bool IsAlive => !IsRetired && CurrentHealth > 0;

    /// <summary>정비 페이즈: 무기 종류 전면 교체 자유. 전투 중에는 StageLoop가 이 메서드 호출 자체를 막아야 한다.</summary>
    public void EquipWeaponFreely(Weapon weapon)
    {
        EquippedWeapon = weapon;
        CurrentHealth = Math.Min(CurrentHealth, MaxHealth);
    }

    /// <summary>전투 중 동종 강화: 필드에서 사용 중인 무기와 동일 종류일 때만 즉시 티어업 허용.</summary>
    public bool TryUpgradeDuringCombat(Weapon mergedWeapon)
    {
        if (EquippedWeapon is null || EquippedWeapon.Type != mergedWeapon.Type)
            return false;

        EquippedWeapon = mergedWeapon;
        return true;
    }

    public void EquipArmor(Armor armor)
    {
        EquippedArmor = armor;
        CurrentHealth = Math.Min(CurrentHealth, MaxHealth);
    }

    public void TakeDamage(double amount)
    {
        if (!IsAlive) return;

        CurrentHealth -= amount;
        if (CurrentHealth <= 0)
        {
            CurrentHealth = 0;
            Retire();
        }
    }

    public void Heal(double amount)
    {
        if (!IsAlive) return;
        CurrentHealth = Math.Min(MaxHealth, CurrentHealth + amount);
    }

    private void Retire()
    {
        IsRetired = true;
        RetireRemainingSeconds = Config.RetireDurationSeconds;
    }

    /// <summary>매 틱 호출. 유물/스킬로 리타이어 시간이 단축된 경우 retireSpeedMultiplier &gt; 1을 전달한다.</summary>
    public void AdvanceRetireTimer(double deltaSeconds, double retireSpeedMultiplier = 1.0)
    {
        if (!IsRetired) return;

        RetireRemainingSeconds -= deltaSeconds * retireSpeedMultiplier;
        if (RetireRemainingSeconds <= 0)
            ReviveNow();
    }

    /// <summary>스테이지 클리어 시 전원 즉시 자동 부활.</summary>
    public void ReviveNow()
    {
        IsRetired = false;
        RetireRemainingSeconds = 0;
        CurrentHealth = MaxHealth;
    }
}
