using DungeonVM.Core.Enums;

namespace DungeonVM.Core.Models;

/// <summary>보석으로 구매하여 무기에 소켓하는 속성 룬. 10스테이지 단위 대형 보스 상성 공략용.</summary>
public sealed class Rune
{
    public ElementType Element { get; }
    public int GemCost { get; }

    public Rune(ElementType element, int gemCost)
    {
        if (element == ElementType.None)
            throw new ArgumentException("룬은 None 속성을 가질 수 없습니다.", nameof(element));

        Element = element;
        GemCost = gemCost;
    }
}
