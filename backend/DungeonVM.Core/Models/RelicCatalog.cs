namespace DungeonVM.Core.Models;

/// <summary>
/// 스테이지 보상 '상자' 선택지의 유물 드롭 풀. 유물 후보 15+10종(전투/수호/회복/재화/자판기/특수 계열,
/// 보스 전용 강력 유물)은 기획 문서에서 아직 확정되지 않아, 기존 RelicEffect 4종을 재사용하는
/// 최소 구성으로 임시 배치했다. 정식 유물 세트가 확정되면 이 풀만 교체하면 된다.
/// </summary>
public static class RelicCatalog
{
    private static readonly Relic[] Pool =
    {
        new("가속의 모래시계", RelicEffect.RetireTimeReduction, 0.2),
        new("탐욕의 반지", RelicEffect.GoldGainBoost, 0.15),
        new("정비공의 할인권", RelicEffect.VendingUpgradeDiscount, 0.2),
        new("그림자 망토", RelicEffect.DodgeChanceBoost, 0.05),
    };

    public static Relic RollRandom(Random rng) => Pool[rng.Next(Pool.Length)];
}
