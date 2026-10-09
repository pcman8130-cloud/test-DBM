using DungeonVM.Core.Balance;
using DungeonVM.Core.Enums;

namespace DungeonVM.Core.Models;

/// <summary>기본 스탯이 없는 빈 껍데기 아바타. 장착한 무기+방어구가 모든 전투 능력을 결정한다.</summary>
public sealed class Character
{
    private static CharacterBalanceSection Config => BalanceProvider.Current.Character;

    private readonly double _metaBaseHealthBonus;
    private double _runBonusAttack;
    private double _runBonusHealth;
    private double _runAttackPercent;      // 능력치 상자: 공격력 %(0.1 = +10%)
    private double _runDodgeBonus;         // 능력치 상자(유틸): 회피율
    private double _runAttackSpeedBonus;   // 능력치 상자(유틸): 공격속도 +비율
    private double _runCooldownReduction;  // 능력치 상자(유틸): 쿨타임 감소(무기 스킬 발동 빈도로 근사)

    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; }
    public Weapon? EquippedWeapon { get; private set; }
    public Armor? EquippedArmor { get; private set; }
    public double CurrentHealth { get; private set; }
    public bool IsRetired { get; private set; }
    public double RetireRemainingSeconds { get; private set; }

    /// <summary>보유 유물 효과의 합(StageLoop가 유물을 얻을 때마다 갱신해서 넘겨준다).</summary>
    public RelicMods RelicMods { get; private set; } = RelicMods.None;

    /// <summary>전투 시작 시 받는 보호막(체력보다 먼저 소모). 전투가 끝나면 사라진다.</summary>
    public double Shield { get; private set; }

    /// <summary>metaBaseHealthBonus: MetaProgression.BaseHealthBonus(영혼 스킬트리 효과)를 그대로 전달.</summary>
    public Character(string name, double metaBaseHealthBonus = 0)
    {
        Name = name;
        _metaBaseHealthBonus = metaBaseHealthBonus;
        CurrentHealth = MaxHealth;
    }

    private static bool IsPhysicalType(WeaponType t) => t is WeaponType.Sword or WeaponType.Shield or WeaponType.Dagger or WeaponType.Bow;
    private static bool IsMagicType(WeaponType t) => t is WeaponType.Staff or WeaponType.Bible;
    private bool IsPhysical => EquippedWeapon is { } w && IsPhysicalType(w.Type);
    private bool IsMagic => EquippedWeapon is { } w && IsMagicType(w.Type);

    public double MaxHealth => (Config.BaseHealth + _metaBaseHealthBonus + _runBonusHealth
        + (EquippedWeapon?.BonusHealth ?? 0) + (EquippedArmor?.BonusHealth ?? 0) + RelicMods.MaxHealthFlat) * (1 + RelicMods.MaxHealthPct);

    public double AttackDamage => ((EquippedWeapon?.Damage ?? 0) + _runBonusAttack) * (1 + _runAttackPercent)
        * (1 + RelicMods.AttackPct + (IsPhysical ? RelicMods.PhysicalAttackPct : 0) + (IsMagic ? RelicMods.MagicAttackPct : 0));

    /// <summary>쿨타임 감소(능력치 상자 + 유물). 마법 무기는 유물의 추가 감소분도 받는다. 별도 쿨타임 스탯이 없어서
    /// 발동형 스킬(단검 암살/방패 밀치기)은 발동 확률 배율로, 마법 무기(지팡이·성서)는 공격(시전) 속도 배율로 근사한다.</summary>
    public double CooldownReduction => _runCooldownReduction + RelicMods.Cdr + (IsMagic ? RelicMods.MagicCdr : 0);

    public double AttacksPerSecond => EquippedWeapon is null ? 0
        : EquippedWeapon.AttacksPerSecond
            * (1 + (EquippedArmor?.AttackSpeedBonus ?? 0) + _runAttackSpeedBonus + RelicMods.AttackSpeedPct + (IsPhysical ? RelicMods.PhysicalAttackSpeedPct : 0))
            * (1 + WeaponSkillCatalog.ApsBonusFor(EquippedWeapon.Type, EquippedWeapon.Tier))
            * (IsMagic ? 1 + CooldownReduction : 1);

    public double DodgeChance => Math.Min(BalanceProvider.Current.Armor.DodgeClampMax, (EquippedArmor?.DodgeChance ?? 0) + _runDodgeBonus + RelicMods.Dodge);
    public RowPosition Row => EquippedWeapon is null ? RowPosition.Front : EquippedWeapon.Row;
    public ElementType Element => EquippedWeapon?.Element ?? ElementType.None;
    public bool IsAlive => !IsRetired && CurrentHealth > 0;

    /// <summary>정비 페이즈: 무기 종류 전면 교체 자유. 전투 중에는 StageLoop가 이 메서드 호출 자체를 막아야 한다.</summary>
    public void EquipWeaponFreely(Weapon weapon)
    {
        EquippedWeapon = weapon;
        CurrentHealth = Math.Min(CurrentHealth, MaxHealth);
    }

    /// <summary>정비 페이즈 전용: 무기를 해제해 맨손 상태로 되돌린다(뒤처진 무기 타입을 포기하고 다음 롤로
    /// 새로 재배정받을 때 사용). 판매/그리드 반환은 호출자 책임.</summary>
    public void UnequipWeapon()
    {
        EquippedWeapon = null;
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

    /// <summary>보호막이 있으면 체력보다 먼저 깎인다.</summary>
    public void TakeDamage(double amount)
    {
        if (!IsAlive) return;

        if (Shield > 0)
        {
            double absorbed = Math.Min(Shield, amount);
            Shield -= absorbed;
            amount -= absorbed;
        }
        if (amount <= 0) return;

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

    /// <summary>전투 시작 시 보호막을 새로 채운다(이전 전투의 보호막은 이월되지 않는다).</summary>
    public void GrantShield(double amount) => Shield = amount;

    /// <summary>유물 효과 합계를 갱신한다(최대 체력이 줄어들 수 있으니 현재 체력을 맞춘다).</summary>
    public void SetRelicMods(RelicMods mods)
    {
        double before = MaxHealth;
        RelicMods = mods;
        double after = MaxHealth;
        if (after > before) CurrentHealth += after - before;
        CurrentHealth = Math.Min(CurrentHealth, after);
    }

    /// <summary>스테이지 보상의 '팀 능력치 영구증가' 선택지 적용. 이번 런 동안 유지되며(영혼 스킬트리와 별개), 체력 증가분만큼 즉시 회복한다.</summary>
    public void AddRunBonus(double attackBonus, double healthBonus)
    {
        _runBonusAttack += attackBonus;
        _runBonusHealth += healthBonus;
        CurrentHealth = Math.Min(MaxHealth, CurrentHealth + healthBonus);
    }

    /// <summary>능력치 상자의 '공격력 %' 결과: 현재 공격력(무기+고정 보너스)에 곱해지며, 이후 무기가 바뀌어도 유지된다.</summary>
    public void AddRunAttackPercent(double percent) => _runAttackPercent += percent;

    /// <summary>능력치 상자의 유틸 결과(회피율). DodgeClampMax까지만 반영된다.</summary>
    public void AddRunDodgeBonus(double amount) => _runDodgeBonus += amount;

    /// <summary>능력치 상자의 유틸 결과(공격속도 +비율).</summary>
    public void AddRunAttackSpeedBonus(double ratio) => _runAttackSpeedBonus += ratio;

    /// <summary>능력치 상자의 유틸 결과(쿨타임 감소).</summary>
    public void AddRunCooldownReduction(double ratio) => _runCooldownReduction += ratio;

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
