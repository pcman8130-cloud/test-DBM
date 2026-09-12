using DungeonVM.Core.Enums;

namespace DungeonVM.Core.Models;

/// <summary>머지판/장착 슬롯에 존재하는 무기 1개체. 동일 Type+Tier 2개를 합치면 Tier+1로 승격한다.</summary>
public sealed class Weapon
{
    public Guid Id { get; } = Guid.NewGuid();
    public WeaponType Type { get; }
    public int Tier { get; private set; }
    public ElementType Element { get; private set; }

    public Weapon(WeaponType type, int tier = 1, ElementType element = ElementType.None)
    {
        if (tier < 1 || tier > WeaponCatalog.MaxTier)
            throw new ArgumentOutOfRangeException(nameof(tier), $"티어는 1~{WeaponCatalog.MaxTier} 사이여야 합니다.");

        Type = type;
        Tier = tier;
        Element = element;
    }

    public RowPosition Row => WeaponCatalog.RowOf(Type);
    public double Damage => WeaponCatalog.DamageAtTier(Type, Tier);
    public double AttacksPerSecond => WeaponCatalog.Get(Type).AttacksPerSecond;
    public double BonusHealth => WeaponCatalog.Get(Type).BonusHealth;
    public bool IsMaxTier => Tier >= WeaponCatalog.MaxTier;

    public bool CanMergeWith(Weapon other) => other.Type == Type && other.Tier == Tier && !IsMaxTier;

    /// <summary>동종 무기 결합. 호출자가 두 원본 인스턴스를 그리드/슬롯에서 제거하고 반환된 새 무기로 교체해야 한다.</summary>
    public Weapon MergeInto(Weapon other)
    {
        if (!CanMergeWith(other))
            throw new InvalidOperationException("동일 무기 종류/티어만 머지할 수 있습니다.");

        var merged = new Weapon(Type, Tier + 1, Element != ElementType.None ? Element : other.Element);
        return merged;
    }

    public void SocketRune(ElementType element) => Element = element;
}
