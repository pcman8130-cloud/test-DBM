namespace DungeonVM.Core.Models;

public enum RelicEffect
{
    RetireTimeReduction,   // 리타이어 시간 단축
    GoldGainBoost,         // 골드 획득량 증가
    VendingUpgradeDiscount,// 자판기 업그레이드 비용 할인
    DodgeChanceBoost,      // 전 캐릭터 회피율 증가
}

/// <summary>슬레이 더 스파이어 형태의 런 전역 패시브 유물. 보석으로 구매.</summary>
public sealed class Relic
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; }
    public RelicEffect Effect { get; }
    public double Magnitude { get; }
    public int GemCost { get; }

    public Relic(string name, RelicEffect effect, double magnitude, int gemCost)
    {
        Name = name;
        Effect = effect;
        Magnitude = magnitude;
        GemCost = gemCost;
    }
}
