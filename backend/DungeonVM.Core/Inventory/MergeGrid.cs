using DungeonVM.Core.Balance;
using DungeonVM.Core.Enums;
using DungeonVM.Core.Models;

namespace DungeonVM.Core.Inventory;

/// <summary>4x4(16칸) 머지 보관함. 시작 시 1x4(4칸)만 해금되고, 이후 4칸 단위로 골드를 소모해 순차 해금한다.</summary>
public sealed class MergeGrid
{
    private static MergeGridBalanceSection Config => BalanceProvider.Current.MergeGrid;

    public static int TotalCells => Config.TotalCells;
    public static int CellsPerUnlock => Config.CellsPerUnlock;

    private readonly Weapon?[] _cells = new Weapon?[TotalCells];

    public int UnlockedCells { get; private set; } = CellsPerUnlock;

    public bool IsCellUnlocked(int index) => index >= 0 && index < UnlockedCells;
    public bool IsFull => Enumerable.Range(0, UnlockedCells).All(i => _cells[i] is not null);
    public bool IsFullyUnlocked => UnlockedCells >= TotalCells;

    public IEnumerable<(int Index, Weapon Weapon)> OccupiedCells()
    {
        for (int i = 0; i < UnlockedCells; i++)
            if (_cells[i] is { } w)
                yield return (i, w);
    }

    public bool TryAddToFirstEmpty(Weapon weapon)
    {
        for (int i = 0; i < UnlockedCells; i++)
        {
            if (_cells[i] is null)
            {
                _cells[i] = weapon;
                return true;
            }
        }
        return false;
    }

    public Weapon? RemoveAt(int index)
    {
        if (!IsCellUnlocked(index)) return null;
        var w = _cells[index];
        _cells[index] = null;
        return w;
    }

    public Weapon? PeekAt(int index) => IsCellUnlocked(index) ? _cells[index] : null;

    /// <summary>그리드 내에서 병합 가능한 첫 번째 동일 무기 쌍을 찾아 즉시 병합한다.</summary>
    public bool TryMergeFirstPair(out Weapon? merged)
    {
        for (int i = 0; i < UnlockedCells; i++)
        {
            if (_cells[i] is not { } a) continue;
            for (int j = i + 1; j < UnlockedCells; j++)
            {
                if (_cells[j] is not { } b) continue;
                if (!a.CanMergeWith(b)) continue;

                merged = a.MergeInto(b);
                _cells[i] = merged;
                _cells[j] = null;
                return true;
            }
        }
        merged = null;
        return false;
    }

    /// <summary>가능한 모든 병합을 반복 수행(연쇄 티어업 포함).</summary>
    public int MergeAllPossible()
    {
        int count = 0;
        while (TryMergeFirstPair(out _)) count++;
        return count;
    }

    /// <summary>지정한 무기(Type+Tier)와 병합 가능한 그리드 내 아이템의 셀 인덱스를 찾는다. 캐릭터 필드 강화용.</summary>
    public int? FindMatchIndex(WeaponType type, int tier)
    {
        for (int i = 0; i < UnlockedCells; i++)
            if (_cells[i] is { } w && w.Type == type && w.Tier == tier)
                return i;
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
