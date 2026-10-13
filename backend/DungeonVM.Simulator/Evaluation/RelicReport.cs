using System.Collections.Concurrent;
using System.Text.Json;
using DungeonVM.Core.Enums;
using DungeonVM.Core.Models;
using DungeonVM.Core.Systems;
using DungeonVM.Simulator.Bots;
using DungeonVM.Simulator.Metrics;

namespace DungeonVM.Simulator.Evaluation;

/// <summary>
/// 유물별 성과 리포트(--relic-report). 같은 봇·같은 시드로 유물을 하나씩 강제해서 돌려 본 결과를 비교한다.
///  - 보스 유물: 그 보스 스테이지를 클리어한 런만 대상으로, 같은 스테이지의 선택지끼리 비교(평균 클리어 스테이지, 20/25/29+ 도달률, 30스테이지 완주율).
///  - 일반 유물: 해금 스테이지(1/11/21)에 도달한 런만 대상으로, 그 시점에 해당 유물을 받았을 때와 못 받았을 때(기준선)를 비교.
/// 평균 플레이어 봇(Balanced) 기준이라 숙련자와는 다를 수 있으니 --bot Expert 등으로도 확인한다.
/// </summary>
public static class RelicReport
{
    private sealed record Row(string Id, string Name, string Kind, int Stage, int Reached, double AvgCleared, double Reach20, double Reach25, double Reach29, double Clear30, double OfferChance = 0,
        int NBible = 0, double AvgWithBible = 0, int NNoBible = 0, double AvgNoBible = 0);

    public static int Run(int runs, int seed, string botName, string? outPath, int stageFilter = 0, string splitWeapon = "Bible")
    {
        Console.WriteLine($"유물별 리포트 — 봇 {botName}, 변형당 {runs}회 (시드 {seed})");

        var jobs = new List<(string Key, RelicDef? Relic, int BossStage)>();
        foreach (int stage in new[] { 5, 10, 15, 20, 25 })
            foreach (var r in RelicCatalog.BossCandidatesFor(stage))
                if (stageFilter == 0 || stageFilter == stage) jobs.Add(($"boss:{r.Id}", r, stage));
        if (stageFilter == 0)
        {
            foreach (int tier in new[] { 1, 11, 21 })
                jobs.Add(($"base:{tier}", null, tier));
            foreach (var r in RelicCatalog.All.Where(x => x.Kind == RelicKind.Regular))
                jobs.Add(($"reg:{r.Id}", r, r.UnlockStage));
        }

        var results = new ConcurrentDictionary<string, List<(int Cleared, bool Bible)>>();
        Parallel.ForEach(jobs, job => results[job.Key] = Simulate(job, runs, seed, botName, splitWeapon));

        var rows = new List<Row>();
        Row Summarize(string id, string name, string kind, int stage, List<(int Cleared, bool Bible)> runsOut, int minCleared)
        {
            var reachedRuns = runsOut.Where(r => r.Cleared >= minCleared).ToList();
            var reached = reachedRuns.Select(r => r.Cleared).ToList();
            int n = Math.Max(reached.Count, 1);
            var withBible = reachedRuns.Where(r => r.Bible).Select(r => (double)r.Cleared).ToList();
            var noBible = reachedRuns.Where(r => !r.Bible).Select(r => (double)r.Cleared).ToList();
            return new Row(id, name, kind, stage, reached.Count, reached.Average(c => (double)c),
                reached.Count(c => c >= 20) / (double)n, reached.Count(c => c >= 25) / (double)n,
                reached.Count(c => c >= 29) / (double)n, reached.Count(c => c >= 30) / (double)n,
                NBible: withBible.Count, AvgWithBible: withBible.Count == 0 ? 0 : withBible.Average(),
                NNoBible: noBible.Count, AvgNoBible: noBible.Count == 0 ? 0 : noBible.Average());
        }

        foreach (var job in jobs)
        {
            var cleared = results[job.Key];
            if (job.Key.StartsWith("boss:"))
                rows.Add(Summarize(job.Relic!.Id, job.Relic.Name, "보스", job.BossStage, cleared, job.BossStage));
            else if (job.Key.StartsWith("base:"))
                rows.Add(Summarize("(기준선)", "유물 없음", "기준", job.BossStage, cleared, job.BossStage - 1));
            else
                rows.Add(Summarize(job.Relic!.Id, job.Relic.Name, "일반", job.Relic.UnlockStage, cleared, job.Relic.UnlockStage - 1) with { OfferChance = OfferChance(job.Relic) });
        }

        Print(rows, splitWeapon);
        Save(rows, runs, seed, botName, outPath);
        return 0;
    }

    /// <summary>특수 상자를 한 번 열 때 이 유물이 후보 3개 안에 들어올 확률(그 유물의 해금 구간 풀 기준, 아직 가진 유물이 없다고 가정).</summary>
    private static double OfferChance(RelicDef relic)
    {
        var pool = RelicCatalog.RegularPoolFor(relic.UnlockStage);
        double relicChance = 1 - DungeonVM.Core.Balance.BalanceProvider.Current.StageRewardChoice.RegularBoxRuneChance;
        double perCandidate = relicChance * relic.Weight / pool.Sum(r => r.Weight);
        return 1 - Math.Pow(1 - perCandidate, 3);
    }

    private static List<(int Cleared, bool Bible)> Simulate((string Key, RelicDef? Relic, int BossStage) job, int runs, int seed, string botName, string splitWeapon)
    {
        var bot = BotCatalog.Create(botName);
        var rng = new Random(seed);
        var meta = new MetaProgression();
        var metrics = new WeaponTierMetrics();
        var attempts = new StageAttemptCollector();
        var cleared = new List<(int Cleared, bool Bible)>(runs);

        bool boss = job.Key.StartsWith("boss:");
        bool regular = job.Key.StartsWith("reg:");

        for (int i = 0; i < runs; i++)
        {
            var result = Program.SimulateOneRun(bot, i, rng, meta, metrics, attempts,
                forceRelic: boss ? (stage, _) => stage == job.BossStage ? job.Relic!.Id : null : null,
                onStageStart: regular ? (stage, loop) =>
                {
                    // 해금 스테이지에 도달한 순간 이 일반 유물을 받은 것으로 친다(실제로는 특수 상자에서 나온 뒤).
                    if (stage == job.Relic!.UnlockStage) loop.GrantRelic(job.Relic);
                } : null);
            cleared.Add((result.StagesCleared, result.FinalWeapons.Any(w => w.StartsWith(splitWeapon))));
            Program.InvestMetaSouls(meta);
        }
        return cleared;
    }

    private static void Print(List<Row> rows, string splitWeapon)
    {
        Console.WriteLine();
        Console.WriteLine("== 보스 유물 (그 보스를 클리어한 런 기준 · 같은 스테이지의 선택지끼리 비교) ==");
        foreach (var group in rows.Where(r => r.Kind == "보스").GroupBy(r => r.Stage))
        {
            double mean = group.Average(r => r.AvgCleared);
            Console.WriteLine($"\n[{group.Key}스테이지]  (대상 런 {group.First().Reached}~{group.Max(r => r.Reached)}개)");
            foreach (var r in group.OrderByDescending(r => r.AvgCleared))
            {
                Console.WriteLine($"  {r.Name,-12} 평균 클리어 {r.AvgCleared,5:F1} ({(r.AvgCleared - mean):+0.0;-0.0;0.0})  20+ {r.Reach20,4:P0}  25+ {r.Reach25,4:P0}  29+ {r.Reach29,4:P0}  30완주 {r.Clear30,5:P1}");
                Console.WriteLine($"{"",16}└ 최종 파티에 {splitWeapon} 있음 {r.AvgWithBible,5:F1} (n={r.NBible}) / 없음 {r.AvgNoBible,5:F1} (n={r.NNoBible})");
            }
        }

        Console.WriteLine("\n== 일반 유물 (해금 스테이지에 도달한 런 기준 · 유물 없음 대비) ==");
        foreach (var group in rows.Where(r => r.Kind != "보스").GroupBy(r => r.Stage))
        {
            var baseline = group.First(r => r.Kind == "기준");
            Console.WriteLine($"\n[{group.Key}스테이지부터]  기준선(유물 없음) 평균 클리어 {baseline.AvgCleared:F1}  20+ {baseline.Reach20:P0}  30완주 {baseline.Clear30:P1}  (대상 런 {baseline.Reached}개)");
            foreach (var r in group.Where(r => r.Kind == "일반").OrderByDescending(r => r.AvgCleared))
                Console.WriteLine($"  {r.Name,-12} 평균 클리어 {r.AvgCleared,5:F1} ({(r.AvgCleared - baseline.AvgCleared):+0.0;-0.0;0.0})  20+ {r.Reach20,4:P0}  25+ {r.Reach25,4:P0}  30완주 {r.Clear30,5:P1}   상자당 등장 {r.OfferChance,5:P1}");
        }
    }

    private static void Save(List<Row> rows, int runs, int seed, string botName, string? outPath)
    {
        string path = outPath ?? Path.Combine(AppContext.BaseDirectory, "relic_report.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { generatedAtUtc = DateTime.UtcNow, bot = botName, runsPerVariant = runs, seed, rows },
            new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        Console.WriteLine($"\n결과를 {path}에 저장했습니다.");
    }
}
