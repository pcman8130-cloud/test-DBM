using DungeonVM.Core.Enums;
using DungeonVM.Core.Models;

namespace DungeonVM.Core.Inventory;

/// <summary>MergeGrid(무기+방어구 공유) + 룬 전용 보관함 + 장착 슬롯 사이의 상호작용을 조율한다.</summary>
public sealed class InventoryManager
{
    public MergeGrid Grid { get; } = new();

    /// <summary>스테이지 클리어 선택보상의 '상자' 결과로 획득한 룬의 전용 보관함(그리드와 별개, 공간 제한 없음).</summary>
    public List<Rune> RuneStorage { get; } = new();

    /// <summary>뽑은 무기를 그리드에 넣는다. 자리가 없으면 false(뽑기 자체가 막히거나 판매를 유도해야 함).</summary>
    public bool ReceiveWeapon(Weapon weapon) => Grid.TryAddWeapon(weapon);

    /// <summary>뽑은 방어구를 그리드에 넣는다. 무기와 같은 칸을 공유하므로 공간 병목의 원인이 된다.</summary>
    public bool ReceiveArmor(Armor armor) => Grid.TryAddArmor(armor);

    public void ReceiveRune(Rune rune) => RuneStorage.Add(rune);

    /// <summary>보관함의 룬 하나를 소모해 무기에 소켓한다. preferredElement가 없으면 아무 룬이나 사용한다.</summary>
    public bool TrySocketRune(Weapon weapon, ElementType? preferredElement = null)
    {
        var rune = preferredElement is { } element
            ? RuneStorage.FirstOrDefault(r => r.Element == element)
            : RuneStorage.FirstOrDefault();

        if (rune is null) return false;

        weapon.SocketRune(rune.Element);
        RuneStorage.Remove(rune);
        return true;
    }

    /// <summary>그리드 내 가능한 모든 병합을 즉시 수행한다. allowMerge로 특정 쌍(예: 룬 보존 대상)의 병합을 막을 수 있다.</summary>
    public int AutoMergeGrid(Func<Weapon, Weapon, bool>? allowMerge = null) => Grid.MergeAllPossible(allowMerge);

    /// <summary>
    /// 전투 중 동종 강화: 그리드에 캐릭터가 현재 장착 중인 무기와 동일한 Type+Tier 완성품이 있으면
    /// 그리드 아이템을 소모해 캐릭터 무기를 즉시 티어업한다. (스펙 4장: "동종 강화 허용")
    /// allowMerge가 지정되고 false를 반환하면(예: 룬 보존을 위해) 강화를 건너뛴다.
    /// </summary>
    public bool TryFieldUpgrade(Character character, Func<Weapon, Weapon, bool>? allowMerge = null)
    {
        if (character.EquippedWeapon is not { } current || current.IsMaxTier)
            return false;

        var matchIndex = Grid.FindMatchIndex(current.Type, current.Tier);
        if (matchIndex is not { } index)
            return false;

        var gridWeapon = (Weapon)Grid.PeekAt(index)!;

        if (allowMerge is not null && !allowMerge(current, gridWeapon))
            return false;

        var merged = current.MergeInto(gridWeapon);

        if (!character.TryUpgradeDuringCombat(merged))
            return false;

        Grid.RemoveAt(index);
        return true;
    }
}
