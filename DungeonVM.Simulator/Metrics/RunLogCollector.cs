namespace DungeonVM.Simulator.Metrics;

/// <summary>3,000회 배치의 개별 런 결과를 모아 봇별 승률/평균 도달 스테이지 등을 집계한다.</summary>
public sealed class RunLogCollector
{
    private readonly List<RunResult> _results = new();

    public IReadOnlyList<RunResult> Results => _results;

    public void Add(RunResult result) => _results.Add(result);

    public double WinRate(string? botName = null)
    {
        var subset = Filter(botName);
        if (subset.Count == 0) return 0;
        return subset.Count(r => r.Outcome == Core.Enums.RunEndReason.Victory) / (double)subset.Count;
    }

    public double AverageStagesCleared(string? botName = null)
    {
        var subset = Filter(botName);
        return subset.Count == 0 ? 0 : subset.Average(r => r.StagesCleared);
    }

    public double AverageFinalGold(string? botName = null)
    {
        var subset = Filter(botName);
        return subset.Count == 0 ? 0 : subset.Average(r => r.FinalGold);
    }

    public IReadOnlyDictionary<Core.Enums.RunEndReason, int> OutcomeBreakdown(string? botName = null)
    {
        var counts = new Dictionary<Core.Enums.RunEndReason, int>();
        foreach (var r in Filter(botName))
            counts[r.Outcome] = counts.GetValueOrDefault(r.Outcome) + 1;
        return counts;
    }

    private List<RunResult> Filter(string? botName)
        => botName is null ? _results : _results.Where(r => r.BotName == botName).ToList();
}
