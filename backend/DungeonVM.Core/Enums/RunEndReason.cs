namespace DungeonVM.Core.Enums;

/// <summary>패배 조건은 출격한 모험가 전원이 동시에 리타이어(전멸)하는 경우뿐이다(자판기 체력 없음).</summary>
public enum RunEndReason
{
    InProgress,
    Victory,
    PartyWiped,
}
