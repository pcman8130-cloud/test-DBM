using DungeonVM.Core.Balance;
using DungeonVM.Core.Enums;

namespace DungeonVM.Core.Combat;

/// <summary>속성 상성 배율 계산. 10스테이지 대형 보스 공략에 필요한 룬 소켓 전략의 근거가 된다.</summary>
public static class DamageCalculator
{
    private static double AdvantageMultiplier => BalanceProvider.Current.Combat.ElementAdvantageMultiplier;
    private static double DisadvantageMultiplier => BalanceProvider.Current.Combat.ElementDisadvantageMultiplier;

    // 3원소 순환 상성: Fire -> Ice -> Lightning -> Fire
    private static readonly Dictionary<ElementType, ElementType> Beats = new()
    {
        [ElementType.Fire] = ElementType.Ice,
        [ElementType.Ice] = ElementType.Lightning,
        [ElementType.Lightning] = ElementType.Fire,
    };

    public static double ElementMultiplier(ElementType attacker, ElementType defender)
    {
        if (attacker == ElementType.None || defender == ElementType.None)
            return 1.0;

        // Holy <-> Dark: 상호 상성(양쪽 다 서로에게 강함)
        if ((attacker == ElementType.Holy && defender == ElementType.Dark) ||
            (attacker == ElementType.Dark && defender == ElementType.Holy))
            return AdvantageMultiplier;

        if (Beats.TryGetValue(attacker, out var beaten) && beaten == defender)
            return AdvantageMultiplier;

        if (Beats.TryGetValue(defender, out var beatsAttacker) && beatsAttacker == attacker)
            return DisadvantageMultiplier;

        return 1.0;
    }

    /// <summary>공격자가 방어자에게 가하는 최종 데미지. 회피 판정은 호출자가 DodgeChance로 먼저 처리한다.</summary>
    public static double ComputeDamage(double baseDamage, ElementType attackerElement, ElementType defenderElement)
        => baseDamage * ElementMultiplier(attackerElement, defenderElement);
}
