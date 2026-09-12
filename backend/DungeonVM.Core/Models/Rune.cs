using DungeonVM.Core.Enums;

namespace DungeonVM.Core.Models;

/// <summary>무기에 소켓하는 속성 룬. 구매 없이 일반/보스 스테이지 클리어 드롭으로만 획득한다.</summary>
public sealed class Rune
{
    public ElementType Element { get; }

    public Rune(ElementType element)
    {
        if (element == ElementType.None)
            throw new ArgumentException("룬은 None 속성을 가질 수 없습니다.", nameof(element));

        Element = element;
    }
}
