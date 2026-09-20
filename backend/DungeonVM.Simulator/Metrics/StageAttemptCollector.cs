using DungeonVM.Core.Enums;

namespace DungeonVM.Simulator.Metrics;

public sealed record StageStat(int Stage, int Attempts, int Wins, double WinRate, double AvgClearSeconds, double AvgRemainingHpRatio);

/// <summary>StageAttempt를 (봇, 스테이지)별로 모아 "스테이지 결과" 표(전투/승리, 판정 승률, 평균 클리어, 평균 잔여 HP)를 만든다.</summary>
public sealed class StageAttemptCollector
{
    private readonly List<StageAttempt> _attempts = new();

    public void Add(StageAttempt attempt) => _attempts.Add(attempt);

    public int TotalAttempts(string? botName = null) => Filter(botName).Count;

    public int TotalWins(string? botName = null) => Filter(botName).Count(a => a.Outcome == RunEndReason.Victory);

    public double OverallWinRate(string? botName = null)
    {
        var subset = Filter(botName);
        return subset.Count == 0 ? 0 : subset.Count(a => a.Outcome == RunEndReason.Victory) / (double)subset.Count;
    }

    public double OverallAvgClearSeconds(string? botName = null)
    {
        var wins = Filter(botName).Where(a => a.Outcome == RunEndReason.Victory).ToList();
        return wins.Count == 0 ? 0 : wins.Average(a => a.ElapsedSeconds);
    }

    /// <summary>스테이지 번호(오름차순) 별 집계. 아직 한 번도 시도되지 않은 스테이지는 포함하지 않는다.</summary>
    public IReadOnlyList<StageStat> ByStage(string botName)
    {
        return _attempts
            .Where(a => a.BotName == botName)
            .GroupBy(a => a.Stage)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                int attempts = g.Count();
                int wins = g.Count(a => a.Outcome == RunEndReason.Victory);
                var winAttempts = g.Where(a => a.Outcome == RunEndReason.Victory).ToList();
                double avgClear = winAttempts.Count == 0 ? 0 : winAttempts.Average(a => a.ElapsedSeconds);
                double avgHp = g.Average(a => a.RemainingHpRatio);
                return new StageStat(g.Key, attempts, wins, attempts == 0 ? 0 : wins / (double)attempts, avgClear, avgHp);
            })
            .ToList();
    }

    private List<StageAttempt> Filter(string? botName)
        => botName is null ? _attempts : _attempts.Where(a => a.BotName == botName).ToList();
}
