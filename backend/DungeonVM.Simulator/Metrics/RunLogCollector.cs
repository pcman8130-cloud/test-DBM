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

    public double AverageGridBottleneckSells(string? botName = null)
    {
        var subset = Filter(botName);
        return subset.Count == 0 ? 0 : subset.Average(r => r.GridBottleneckSells);
    }

    public double AverageRuneAvoidanceSkips(string? botName = null)
    {
        var subset = Filter(botName);
        return subset.Count == 0 ? 0 : subset.Average(r => r.RuneAvoidanceSkips);
    }

    /// <summary>런 종료 시점 무기 레벨을 5/10/15 구간으로 나눠 표본 대비 비율을 낸다 (도달률이 아닌 "정체 지점" 스냅샷).</summary>
    public IReadOnlyDictionary<string, double> LevelBucketDistribution(string? botName = null)
    {
        var weapons = Filter(botName).SelectMany(r => r.FinalWeapons).ToList();
        if (weapons.Count == 0) return new Dictionary<string, double>();

        var buckets = new Dictionary<string, int> { ["1-4"] = 0, ["5-9"] = 0, ["10-14"] = 0, ["15"] = 0 };
        foreach (var w in weapons)
        {
            int tIndex = w.LastIndexOf('T');
            if (tIndex < 0 || !int.TryParse(w[(tIndex + 1)..], out int tier)) continue;

            string bucket = tier switch
            {
                < 5 => "1-4",
                < 10 => "5-9",
                < 15 => "10-14",
                _ => "15",
            };
            buckets[bucket]++;
        }

        return buckets.ToDictionary(kv => kv.Key, kv => kv.Value / (double)weapons.Count);
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
