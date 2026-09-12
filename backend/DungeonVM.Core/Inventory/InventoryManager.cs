using DungeonVM.Core.Models;

namespace DungeonVM.Core.Inventory;

/// <summary>MergeGrid + 장착 슬롯 사이의 상호작용(자동 머지, 전투 중 동종 강화 드래그)을 조율한다.</summary>
public sealed class InventoryManager
{
    public MergeGrid Grid { get; } = new();

    /// <summary>뽑은 무기를 그리드에 넣는다. 자리가 없으면 false(뽑기 자체가 막히거나 판매를 유도해야 함).</summary>
    public bool ReceiveWeapon(Weapon weapon) => Grid.TryAddToFirstEmpty(weapon);

    /// <summary>그리드 내 가능한 모든 병합을 즉시 수행한다.</summary>
    public int AutoMergeGrid() => Grid.MergeAllPossible();

    /// <summary>
    /// 전투 중 동종 강화: 그리드에 캐릭터가 현재 장착 중인 무기와 동일한 Type+Tier 완성품이 있으면
    /// 그리드 아이템을 소모해 캐릭터 무기를 즉시 티어업한다. (스펙 4장: "동종 강화 허용")
    /// </summary>
    public bool TryFieldUpgrade(Character character)
    {
        if (character.EquippedWeapon is not { } current || current.IsMaxTier)
            return false;

        var matchIndex = Grid.FindMatchIndex(current.Type, current.Tier);
        if (matchIndex is not { } index)
            return false;

        var gridWeapon = Grid.PeekAt(index)!;
        var merged = current.MergeInto(gridWeapon);

        if (!character.TryUpgradeDuringCombat(merged))
            return false;

        Grid.RemoveAt(index);
        return true;
    }
}
