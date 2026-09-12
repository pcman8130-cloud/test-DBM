using DungeonVM.Core.Balance;
using DungeonVM.Core.Enums;
using DungeonVM.Core.Models;

namespace DungeonVM.Core.Inventory;

/// <summary>
/// 4x4(16칸) 머지 보관함. 시작 시 1x4(4칸)만 해금되고, 이후 4칸 단위로 골드를 소모해 순차 해금한다.
/// 무기 재료와 완제품 방어구가 같은 칸을 공유해 공간 병목을 유발한다(룬은 별도 전용 보관함 - InventoryManager.RuneStorage).
/// </summary>
public sealed class MergeGrid
{
    private static MergeGridBalanceSection Config => BalanceProvider.Current.MergeGrid;

    public static int TotalCells => Config.TotalCells;
    public static int CellsPerUnlock => Config.CellsPerUnlock;

    // Weapon 또는 Armor만 들어간다.
    private readonly object?[] _cells = new object?[TotalCells];

    public int UnlockedCells { get; private set; } = CellsPerUnlock;

    public bool IsCellUnlocked(int index) => index >= 0 && index < UnlockedCells;
    public bool IsFull => Enumerable.Range(0, UnlockedCells).All(i => _cells[i] is not null);
    public bool IsFullyUnlocked => UnlockedCells >= TotalCells;

    public IEnumerable<(int Index, object Item)> OccupiedCells()
    {
        for (int i = 0; i < UnlockedCells; i++)
            if (_cells[i] is { } item)
                yield return (i, item);
    }

    public IEnumerable<(int Index, Weapon Weapon)> OccupiedWeapons()
    {
        foreach (var (i, item) in OccupiedCells())
            if (item is Weapon w)
                yield return (i, w);
    }

    public IEnumerable<(int Index, Armor Armor)> OccupiedArmors()
    {
        foreach (var (i, item) in OccupiedCells())
            if (item is Armor a)
                yield return (i, a);
    }

    public bool TryAddWeapon(Weapon weapon) => TryAdd(weapon);
    public bool TryAddArmor(Armor armor) => TryAdd(armor);

    private bool TryAdd(object item)
    {
        for (int i = 0; i < UnlockedCells; i++)
        {
            if (_cells[i] is null)
            {
                _cells[i] = item;
                return true;
            }
        }
        return false;
    }

    /// <summary>셀 내용물을 종류 상관없이 제거한다. 호출자가 반환된 object의 실제 타입(Weapon/Armor)을 알고 있어야 한다.</summary>
    public object? RemoveAt(int index)
    {
        if (!IsCellUnlocked(index)) return null;
        var item = _cells[index];
        _cells[index] = null;
        return item;
    }

    public object? PeekAt(int index) => IsCellUnlocked(index) ? _cells[index] : null;

    /// <summary>
    /// 그리드 내에서 병합 가능한 첫 번째 동일 무기 쌍을 찾아 즉시 병합한다.
    /// allowMerge가 주어지면 그 쌍은 건너뛰고 다른 병합 가능 쌍을 계속 찾는다(룬 보존을 위한 선택적 머지 회피용).
    /// </summary>
    public bool TryMergeFirstPair(Func<Weapon, Weapon, bool>? allowMerge, out Weapon? merged)
    {
        var weapons = OccupiedWeapons().ToList();
        for (int a = 0; a < weapons.Count; a++)
        {
            for (int b = a + 1; b < weapons.Count; b++)
            {
                var (indexA, weaponA) = weapons[a];
                var (indexB, weaponB) = weapons[b];
                if (!weaponA.CanMergeWith(weaponB)) continue;
                if (allowMerge is not null && !allowMerge(weaponA, weaponB)) continue;

                merged = weaponA.MergeInto(weaponB);
                _cells[indexA] = merged;
                _cells[indexB] = null;
                return true;
            }
        }
        merged = null;
        return false;
    }

    public bool TryMergeFirstPair(out Weapon? merged) => TryMergeFirstPair(null, out merged);

    /// <summary>가능한 모든 병합을 반복 수행(연쇄 티어업 포함). allowMerge로 특정 쌍의 병합을 선택적으로 막을 수 있다.</summary>
    public int MergeAllPossible(Func<Weapon, Weapon, bool>? allowMerge = null)
    {
        int count = 0;
        while (TryMergeFirstPair(allowMerge, out _)) count++;
        return count;
    }

    /// <summary>지정한 무기(Type+Tier)와 병합 가능한 그리드 내 아이템의 셀 인덱스를 찾는다. 캐릭터 필드 강화용.</summary>
    public int? FindMatchIndex(WeaponType type, int tier)
    {
        foreach (var (index, weapon) in OccupiedWeapons())
            if (weapon.Type == type && weapon.Tier == tier)
                return index;
        return null;
    }

    public static int NextUnlockGoldCost(int unlockedCells)
    {
        int blockIndex = unlockedCells / CellsPerUnlock; // 1 = 2번째 블록(칸 5~8), 2 = 3번째, 3 = 4번째
        var costs = Config.UnlockGoldCosts;
        return blockIndex >= 1 && blockIndex <= costs.Count ? costs[blockIndex - 1] : int.MaxValue;
    }

    public int NextUnlockGoldCost() => NextUnlockGoldCost(UnlockedCells);

    /// <summary>골드 차감은 호출자가 책임지고, 성공 시에만 다음 4칸 블록을 연다.</summary>
    public void UnlockNextBlock() => UnlockedCells = Math.Min(TotalCells, UnlockedCells + CellsPerUnlock);
}
