using DungeonVM.Core.Balance;
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

    /// <summary>4x4 그리드(무기+방어구 공유)가 가득 차 새로 얻은 아이템을 강제로 팔아치운 횟수. 그리드 병목 메트릭용.</summary>
    public int GridBottleneckSells { get; private set; }

    /// <summary>룬이 소켓된 무기를 보존하기 위해 유효한 머지를 의도적으로 건너뛴 횟수. 룬 소멸 회피 메트릭용.</summary>
    public int RuneAvoidanceSkips { get; private set; }

    /// <summary>ShouldSaveFor 판단으로 재뽑기 등 할인 소비를 멈추고 저축하기로 한 횟수. 저축 로직 작동 확인용.</summary>
    public int SavingsHolds { get; private set; }

    public BotContext(StageLoop stageLoop, Random rng, MetaProgression meta)
    {
        StageLoop = stageLoop;
        Rng = rng;
        Meta = meta;
    }

    /// <summary>
    /// 비용이 들지 않는 행동(그리드 자동 머지, 전투 중 동종 강화)은 모든 봇이 항상 수행한다.
    /// allowMerge를 지정하면 특정 쌍의 병합을 선택적으로 막을 수 있다(룬 보존 등). false를 반환한 쌍은
    /// RuneAvoidanceSkips에 자동 집계된다.
    /// </summary>
    public void ApplyFreeActions(Func<Weapon, Weapon, bool>? allowMerge = null)
    {
        Func<Weapon, Weapon, bool>? tracked = allowMerge is null
            ? null
            : (a, b) =>
            {
                bool allowed = allowMerge(a, b);
                if (!allowed) RuneAvoidanceSkips++;
                return allowed;
            };

        Inventory.AutoMergeGrid(tracked);
        foreach (var c in Party)
            Inventory.TryFieldUpgrade(c, tracked);
    }

    /// <summary>지금 스테이지부터 stagesAhead개 스테이지의 클리어 기본 골드 수입(스테이지 선택보상 제외) 합산 예상치.</summary>
    public int ProjectedIncome(int stagesAhead)
    {
        var cfg = BalanceProvider.Current.StageLoop;
        int total = 0;
        for (int i = 0; i < stagesAhead; i++)
        {
            int stage = Math.Min(StageLoop.CurrentStage + i, Core.Systems.StageLoop.MaxStage);
            total += cfg.VictoryGoldBase + stage * cfg.VictoryGoldPerStage;
        }
        return total;
    }

    /// <summary>
    /// 목표 비용이 지금 당장은 부족하지만 앞으로 stagesAhead 스테이지 수입 안에 모일 것으로 예상되면 true.
    /// 이 경우 봇은 재뽑기 등 할인 소비를 멈추고 저축해야 한다 — 안 그러면 정비 페이즈마다 남는 돈을
    /// 전부 소비해버려서 목표 금액이 영원히 모이지 않는다(예: 150골드 업그레이드가 스테이지 수입만으로는
    /// 14스테이지는 지나야 한 번에 감당되는데, 매번 남는 돈을 재뽑기에 다 쓰면 그 시점이 와도 못 삼).
    /// </summary>
    public bool ShouldSaveFor(int goalCost, int stagesAhead = 2)
    {
        int shortfall = goalCost - Currency.Gold;
        if (shortfall <= 0) return false;

        bool shouldSave = shortfall <= ProjectedIncome(stagesAhead);
        if (shouldSave) SavingsHolds++;
        return shouldSave;
    }

    public bool TryRollWeapon()
    {
        if (!Currency.TrySpend(CurrencyType.Gold, VendingMachine.WeaponRollCost))
            return false;

        var weapon = Machine.RollWeapon(Rng);
        if (!Inventory.ReceiveWeapon(weapon))
            GridBottleneckSells++; // 그리드(무기+방어구 공유)가 가득 차 환급 없이 즉시 폐기(판매 환급을 주면 롤-판매 무한 차익 루프가 생긴다)

        return true;
    }

    public bool TryUpgradeAttack()
    {
        if (!Machine.CanUpgradeAttack) return false;
        int cost = (int)(Machine.AttackUpgradeCost * (1 - StageLoop.RelicUpgradeDiscountRatio));
        if (!Currency.TrySpend(CurrencyType.Gold, cost)) return false;
        Machine.UpgradeAttackLevel();
        return true;
    }

    public bool TryUpgradeDefense()
    {
        if (!Machine.CanUpgradeDefense) return false;
        int cost = (int)(Machine.DefenseUpgradeCost * (1 - StageLoop.RelicUpgradeDiscountRatio));
        if (!Currency.TrySpend(CurrencyType.Gold, cost)) return false;
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

    /// <summary>골드로 새 캐릭터 슬롯을 해금한다(초기 2명 → 최대 5명).</summary>
    public bool TryUnlockCharacterSlot()
    {
        if (!StageLoop.CanUnlockCharacterSlot) return false;
        if (!Currency.TrySpend(CurrencyType.Gold, StageLoop.NextCharacterSlotGoldCost())) return false;
        StageLoop.UnlockCharacterSlot(Meta.BaseHealthBonus);
        return true;
    }

    /// <summary>보관함에 룬이 있고 캐릭터 무기가 아직 비어있으면 소켓한다. 룬은 구매 없이 스테이지/보스 드롭으로만 얻는다.</summary>
    public bool TrySocketRuneOn(Character character)
    {
        if (character.EquippedWeapon is not { } weapon || weapon.Element != ElementType.None)
            return false;

        return Inventory.TrySocketRune(weapon);
    }

    public bool TryRollArmor()
    {
        if (!Currency.TrySpend(CurrencyType.Gold, VendingMachine.ArmorRollCost))
            return false;

        var armor = Machine.RollArmor(Rng);
        if (!Inventory.ReceiveArmor(armor))
            GridBottleneckSells++; // 그리드(무기+방어구 공유)가 가득 차 환급 없이 즉시 폐기

        return true;
    }

    /// <summary>정비 페이즈 전용: 그리드에서 필터를 만족하는 최고 티어 무기를 캐릭터에 장착(전면 교체).</summary>
    public bool TryEquipFromGrid(Character character, Func<Weapon, bool>? filter = null)
    {
        int bestIndex = -1;
        int bestTier = -1;

        foreach (var (index, weapon) in Inventory.Grid.OccupiedWeapons())
        {
            if (filter is not null && !filter(weapon)) continue;
            if (weapon.Tier <= bestTier) continue;

            bestIndex = index;
            bestTier = weapon.Tier;
        }

        if (bestIndex < 0) return false;

        var removed = (Weapon)Inventory.Grid.RemoveAt(bestIndex)!;

        if (character.EquippedWeapon is { } old && !Inventory.ReceiveWeapon(old))
        {
            Currency.SellWeapon(old);
            GridBottleneckSells++;
        }

        character.EquipWeaponFreely(removed);
        return true;
    }

    /// <summary>정비 페이즈 전용: 그리드에서 필터를 만족하는 최고 등급 방어구를 캐릭터에 장착(전면 교체).</summary>
    public bool TryEquipArmorFromGrid(Character character, Func<Armor, bool>? filter = null)
    {
        int bestIndex = -1;
        var bestRarity = (ArmorRarity)(-1);

        foreach (var (index, armor) in Inventory.Grid.OccupiedArmors())
        {
            if (filter is not null && !filter(armor)) continue;
            if (armor.Rarity <= bestRarity) continue;

            bestIndex = index;
            bestRarity = armor.Rarity;
        }

        if (bestIndex < 0) return false;

        var removed = (Armor)Inventory.Grid.RemoveAt(bestIndex)!;

        if (character.EquippedArmor is { } current && current.Rarity >= removed.Rarity)
        {
            // 기존 방어구가 더 좋으면 새로 뽑은 걸 되돌려놓거나(자리 없으면 판매) 장착하지 않는다.
            if (!Inventory.ReceiveArmor(removed))
            {
                Currency.SellArmor(removed);
                GridBottleneckSells++;
            }
            return false;
        }

        if (character.EquippedArmor is { } old && !Inventory.ReceiveArmor(old))
        {
            Currency.SellArmor(old);
            GridBottleneckSells++;
        }

        character.EquipArmor(removed);
        return true;
    }
}
