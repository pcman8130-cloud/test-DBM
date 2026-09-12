using DungeonVM.Core.Enums;
using DungeonVM.Core.Models;

namespace DungeonVM.Core.Combat;

/// <summary>장착 무기 사거리(전열/후열)에 따라 자동 진형을 구성하고, 몬스터의 우선 타겟을 결정한다.</summary>
public static class FormationManager
{
    public static IReadOnlyList<Character> FrontRow(IEnumerable<Character> party)
        => party.Where(c => c.Row == RowPosition.Front).ToList();

    public static IReadOnlyList<Character> BackRow(IEnumerable<Character> party)
        => party.Where(c => c.Row == RowPosition.Back).ToList();

    /// <summary>몬스터는 캐릭터를 최우선 공격(전열 우선)하고, 방어선(파티) 전멸 시에만 자판기를 공격한다.</summary>
    public static Character? SelectMonsterTarget(IReadOnlyList<Character> party)
    {
        var front = party.Where(c => c.IsAlive && c.Row == RowPosition.Front).ToList();
        if (front.Count > 0) return front[0];

        var back = party.Where(c => c.IsAlive && c.Row == RowPosition.Back).ToList();
        return back.Count > 0 ? back[0] : null;
    }

    public static bool PartyWiped(IReadOnlyList<Character> party) => party.All(c => !c.IsAlive);
}
