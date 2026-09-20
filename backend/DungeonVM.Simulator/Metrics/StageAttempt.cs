using DungeonVM.Core.Enums;

namespace DungeonVM.Simulator.Metrics;

/// <summary>
/// 스테이지 1회 전투 시도의 결과. 런이 이어지든(Victory) 여기서 끝나든(PartyWiped) 전투가 한 번
/// 벌어질 때마다 기록해서, "스테이지별" 승률/평균 클리어 시간/평균 잔여 체력을 낼 수 있게 한다.
/// </summary>
public sealed record StageAttempt(
    string BotName,
    int Stage,
    RunEndReason Outcome,
    double ElapsedSeconds,
    double RemainingHpRatio
);
