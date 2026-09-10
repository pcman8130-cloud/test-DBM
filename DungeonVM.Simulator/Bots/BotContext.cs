using DungeonVM.Core.Enums;
using DungeonVM.Core.Models;
using DungeonVM.Core.Systems;

namespace DungeonVM.Simulator.Bots;

/// <summary>봇이 매 결정 시점에 조작하는 런 상태에 대한 얇은 파사드.</summary>
public sealed class BotContext
{
    public StageLoop StageLoop { get; }
    public Random Rng { get; }
    public MetaProgression Meta { get; }

    public CurrencyManager Currency => StageLoop.Currency;
    public Core.Inventory.InventoryManager Inventory => StageLoop.Inventory;
    public VendingMachine Machine => StageLoop.Machine;
    public List<Character> Party => StageLoop.Party;

    public BotContext(StageLoop stageLoop, Random rng, MetaProgression meta)
    {
        StageLoop = stageLoop;
        Rng = rng;
        Meta = meta;
    }

    /// <summary>비용이 들지 않는 행동(그리드 자동 머지, 전투 중 동종 강화)은 모든 봇이 항상 수행한다.</summary>
    public void ApplyFreeActions()
    {
        Inventory.AutoMergeGrid();
        foreach (var c in Party)
            Inventory.TryFieldUpgrade(c);
    }

    public bool TryRollWeapon()
    {
        if (!Currency.TrySpend(CurrencyType.Gold, VendingMachine.WeaponRollCost))
            return false;

        var weapon = Machine.RollWeapon(Rng);
        if (!Inventory.ReceiveWeapon(weapon))
            Currency.SellWeapon(weapon); // 그리드가 가득 찼으면 즉시 판매 환급

        return true;
    }

    public bool TryUpgradeAttack()
    {
        if (!Machine.CanUpgradeAttack) return false;
        if (!Currency.TrySpend(CurrencyType.Gold, Machine.AttackUpgradeCost)) return false;
        Machine.UpgradeAttackLevel();
        return true;
    }

    public bool TryUpgradeDefense()
    {
        if (!Machine.CanUpgradeDefense) return false;
        if (!Currency.TrySpend(CurrencyType.Gold, Machine.DefenseUpgradeCost)) return false;
        Machine.UpgradeDefenseLevel();
        return true;
    }

    public bool TryUnlockGrid()
    {
        if (Inventory.Grid.IsFullyUnlocked) return false;
        if (!Currency.TrySpend(CurrencyType.Gold, Inventory.Grid.NextUnlockGoldCost())) return false;
        Inventory.Grid.UnlockNextBlock();
        return true;
    }

    public bool TryRollArmor()
    {
        if (!Currency.TrySpend(CurrencyType.Gold, VendingMachine.ArmorRollCost))
            return false;

        var armor = Machine.RollArmor(Rng);
        EquipArmorOnBestTarget(armor);
        return true;
    }

    private void EquipArmorOnBestTarget(Armor armor)
    {
        var target = Party.OrderBy(c => c.EquippedArmor?.Rarity ?? (ArmorRarity)(-1)).First();

        if (target.EquippedArmor is { } current && current.Rarity >= armor.Rarity)
        {
            Currency.SellArmor(armor);
            return;
        }

        if (target.EquippedArmor is { } replaced)
            Currency.SellArmor(replaced);

        target.EquipArmor(armor);
    }

    /// <summary>정비 페이즈 전용: 그리드에서 필터를 만족하는 최고 티어 무기를 캐릭터에 장착(전면 교체).</summary>
    public bool TryEquipFromGrid(Character character, Func<Weapon, bool>? filter = null)
    {
        int bestIndex = -1;
        int bestTier = -1;

        foreach (var (index, weapon) in Inventory.Grid.OccupiedCells())
        {
            if (filter is not null && !filter(weapon)) continue;
            if (weapon.Tier <= bestTier) continue;

            bestIndex = index;
            bestTier = weapon.Tier;
        }

        if (bestIndex < 0) return false;

        var removed = Inventory.Grid.RemoveAt(bestIndex)!;

        if (character.EquippedWeapon is { } old && !Inventory.ReceiveWeapon(old))
            Currency.SellWeapon(old);

        character.EquipWeaponFreely(removed);
        return true;
    }
}
