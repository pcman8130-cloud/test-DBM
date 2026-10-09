using System.Collections.Concurrent;
using System.Text.Json;
using DungeonVM.Core.Balance;
using DungeonVM.Core.Enums;
using DungeonVM.Core.Models;
using DungeonVM.Core.Systems;
using DungeonVM.Simulator.Bots;
using DungeonVM.Simulator.Metrics;

namespace DungeonVM.Simulator.Evaluation;

/// <summary>목표 대비 측정값 1건. Deviation은 허용 범위 밖으로 벗어난 거리(범위 안이면 0, 미달이면 음수, 초과면 양수).</summary>
public sealed record MetricResult(
    string Group, string Metric, double? Value, double? Min, double? Max,
    string Status, double Deviation, int Sample, string? Detail, bool Draft);

/// <summary>
/// BalanceTargets.json(목표 지표)을 읽고, 균형형 봇 배치 시뮬레이션으로 측정한 값과 비교해 "목표에서 벗어난
/// 지표" 목록을 만든다. 스테이지 승률 등 기본 지표는 균형형 봇 하나로 재고, playstyles 섹션이 있으면 저축형/즉시 강화형
/// 같은 다른 플레이 성향 봇도 돌려서 "어느 한 성향만 망가지거나 압도적이지 않은지"를 추가로 본다. 이 결과(balance_eval.json)가 이후 LLM 밸런싱 단계의 입력이 된다.
/// 같은 시드면 항상 같은 결과가 나오도록 모든 난수를 고정 시드로 만든다(수치 변경 전/후 비교의 노이즈 제거).
/// </summary>
public static class BalanceEvaluator
{
    private const double MinStageSample = 30;

    public static int Run(int runs, int relicRuns, bool skipRelics, int seed, string? targetsPath, string? evalOutPath)
    {
        string resolvedTargets = ResolveTargetsPath(targetsPath);
        using var targetsDoc = JsonDocument.Parse(File.ReadAllText(resolvedTargets));
        var targets = targetsDoc.RootElement;

        Console.WriteLine($"밸런스 평가기 — 목표: {resolvedTargets}");
        Console.WriteLine($"균형형 봇 {runs}회 (시드 {seed}) 측정 중... (playstyles가 있으면 성향별 봇도 같은 횟수로 측정)");
        if (skipRelics) Console.WriteLine("유물 격차 측정은 --skip-relics로 건너뜁니다.");
        else Console.WriteLine($"유물 격차 측정은 선택지별 {relicRuns}회로 진행합니다.");

        var results = EvaluateAll(targets, runs, relicRuns, skipRelics, seed);

        PrintReport(results);
        WriteJson(results, runs, relicRuns, skipRelics, seed, resolvedTargets, evalOutPath);
        return 0;
    }

    /// <summary>현재 BalanceProvider.Current 수치로 모든 지표를 측정해 목표와 비교한다(출력 없음 — 튜너가 반복 호출).</summary>
    public static List<MetricResult> EvaluateAll(JsonElement targets, int runs, int relicRuns, bool skipRelics, int seed)
    {
        var measured = MeasureBaseline(runs, seed);
        var results = new List<MetricResult>();

        EvaluateRunClear(targets, measured, results);
        EvaluateStageWinRates(targets, measured, results);
        EvaluateWeaponShare(targets, measured, results);
        EvaluateGrowth(targets, measured, results);
        EvaluatePlaystyles(targets, measured, runs, seed, results);
        EvaluateSkillTiers(targets, runs, seed, results);
        if (!skipRelics)
            EvaluateRelicBalance(targets, relicRuns, seed, results);

        return results;
    }

    /// <summary>튜닝 점수(낮을수록 좋음): 목표를 벗어난 지표의 정규화된 이탈 거리 합. 초안(draft) 지표와, 현재 화이트리스트
    /// 파라미터로는 움직일 수 없는 '유물 격차'는 점수에서 제외한다(리포트에는 계속 표시).</summary>
    public static double Score(IEnumerable<MetricResult> results)
        => results.Where(IsScored).Sum(NormalizedViolation);

    public static bool IsScored(MetricResult r)
        => r.Status is "LOW" or "HIGH" && !r.Draft && r.Group != "유물 격차";

    private static double NormalizedViolation(MetricResult r)
    {
        double violatedBound = r.Status == "LOW" ? r.Min ?? 0 : r.Max ?? 0;
        return Math.Abs(r.Deviation) / Math.Max(Math.Abs(violatedBound), 0.05);
    }

    // ───────────────────────── 측정 ─────────────────────────

    private sealed class Baseline
    {
        public int Runs;
        public int Clears;
        public IReadOnlyList<StageStat> Stages = Array.Empty<StageStat>();
        public WeaponTierMetrics Weapons = new();
        public Dictionary<int, List<int>> MaxTierAtStage = new();
        public int TotalBottleneckSells;
        public int TotalStagesCleared;
    }

    private static Baseline MeasureBaseline(int runs, int seed)
    {
        var bot = new BalancedBot();
        var rng = new Random(seed);
        var meta = new MetaProgression();
        var weapons = new WeaponTierMetrics();
        var stageAttempts = new StageAttemptCollector();
        var b = new Baseline { Runs = runs, Weapons = weapons };

        void OnStageStart(int stage, StageLoop loop)
        {
            if (stage != 10 && stage != 20 && stage != 30) return;
            int maxTier = loop.Party.Where(c => c.EquippedWeapon is not null)
                .Select(c => c.EquippedWeapon!.Tier).DefaultIfEmpty(0).Max();
            if (!b.MaxTierAtStage.TryGetValue(stage, out var list)) b.MaxTierAtStage[stage] = list = new List<int>();
            list.Add(maxTier);
        }

        for (int i = 0; i < runs; i++)
        {
            var result = Program.SimulateOneRun(bot, i, rng, meta, weapons, stageAttempts, onStageStart: OnStageStart);
            if (result.Outcome == RunEndReason.Victory) b.Clears++;
            b.TotalBottleneckSells += result.GridBottleneckSells;
            b.TotalStagesCleared += result.StagesCleared;
            Program.InvestMetaSouls(meta);
        }

        b.Stages = stageAttempts.ByStage(bot.Name);
        return b;
    }

    private static double RelicBatchClearRate(int stage, string relicId, int runs, int seed)
    {
        var bot = new BalancedBot();
        var rng = new Random(seed);
        var meta = new MetaProgression();
        var metrics = new WeaponTierMetrics();
        var attempts = new StageAttemptCollector();
        int clears = 0;

        for (int i = 0; i < runs; i++)
        {
            var result = Program.SimulateOneRun(bot, i, rng, meta, metrics, attempts,
                forceRelic: (cleared, _) => cleared == stage ? relicId : null);
            if (result.Outcome == RunEndReason.Victory) clears++;
            Program.InvestMetaSouls(meta);
        }
        return clears / (double)runs;
    }

    // ───────────────────────── 평가 ─────────────────────────

    private static void EvaluateRunClear(JsonElement targets, Baseline m, List<MetricResult> results)
    {
        if (!targets.TryGetProperty("runClearRate", out var t)) return;
        results.Add(Judge("전체", "runClearRate", m.Clears / (double)m.Runs, Num(t, "min"), Num(t, "max"), m.Runs, 1, null, false));
    }

    private static void EvaluateStageWinRates(JsonElement targets, Baseline m, List<MetricResult> results)
    {
        if (!targets.TryGetProperty("stageWinRate", out var swr) || !swr.TryGetProperty("bands", out var bands)) return;
        var byStage = m.Stages.ToDictionary(s => s.Stage);

        foreach (var band in bands.EnumerateArray())
        {
            string name = band.GetProperty("name").GetString() ?? "";
            double? min = Num(band, "min"), max = Num(band, "max");
            foreach (var stageEl in band.GetProperty("stages").EnumerateArray())
            {
                int stage = stageEl.GetInt32();
                if (byStage.TryGetValue(stage, out var stat))
                    results.Add(Judge(name, $"stageWinRate.{stage}", stat.WinRate, min, max, stat.Attempts, (int)MinStageSample,
                        $"{stat.Wins}/{stat.Attempts}", false));
                else
                    results.Add(Judge(name, $"stageWinRate.{stage}", null, min, max, 0, (int)MinStageSample, "도달한 런 없음", false));
            }
        }
    }

    private static void EvaluateWeaponShare(JsonElement targets, Baseline m, List<MetricResult> results)
    {
        if (!targets.TryGetProperty("weaponTypeShare", out var t)) return;
        double defMin = Num(t.GetProperty("default"), "min") ?? 0, defMax = Num(t.GetProperty("default"), "max") ?? 1;
        int total = m.Weapons.TotalWeaponObservations;

        foreach (WeaponType type in Enum.GetValues(typeof(WeaponType)))
        {
            double min = defMin, max = defMax;
            if (t.TryGetProperty("overrides", out var ov) && ov.TryGetProperty(type.ToString(), out var o))
            {
                min = Num(o, "min") ?? min;
                max = Num(o, "max") ?? max;
            }

            int count = m.Weapons.WeaponEquipCounts.Where(kv => kv.Key.Type == type).Sum(kv => kv.Value);
            results.Add(Judge("무기 종류 채택률", $"weaponTypeShare.{type}", total == 0 ? null : count / (double)total, min, max, total, 1, null, false));
        }
    }

    private static void EvaluateGrowth(JsonElement targets, Baseline m, List<MetricResult> results)
    {
        if (!targets.TryGetProperty("growth", out var g)) return;
        bool draft = g.TryGetProperty("status", out var st) && (st.GetString() ?? "").StartsWith("draft", StringComparison.Ordinal);

        if (g.TryGetProperty("avgMaxWeaponTierAtStage", out var arr))
        {
            foreach (var item in arr.EnumerateArray())
            {
                int stage = item.GetProperty("stage").GetInt32();
                double? min = Num(item, "min"), max = Num(item, "max");
                if (m.MaxTierAtStage.TryGetValue(stage, out var list) && list.Count > 0)
                    results.Add(Judge("무기 성장", $"avgMaxWeaponTier.stage{stage}", list.Average(), min, max, list.Count, (int)MinStageSample, null, draft));
                else
                    results.Add(Judge("무기 성장", $"avgMaxWeaponTier.stage{stage}", null, min, max, 0, (int)MinStageSample, "도달한 런 없음", draft));
            }
        }

        if (g.TryGetProperty("gridBottleneckSellsPerClearedStage", out var gb))
        {
            double? v = m.TotalStagesCleared == 0 ? null : m.TotalBottleneckSells / (double)m.TotalStagesCleared;
            results.Add(Judge("무기 성장", "gridBottleneckSellsPerClearedStage", v, null, Num(gb, "max"), m.TotalStagesCleared, 1, null, draft));
        }
    }

    private sealed record StyleStat(string Bot, int Runs, int Clears, double AvgStagesCleared);

    private static StyleStat MeasureStyle(IBot bot, int runs, int seed)
    {
        var rng = new Random(seed);
        var meta = new MetaProgression();
        var weapons = new WeaponTierMetrics();
        var attempts = new StageAttemptCollector();
        int clears = 0, stagesCleared = 0;

        for (int i = 0; i < runs; i++)
        {
            var result = Program.SimulateOneRun(bot, i, rng, meta, weapons, attempts);
            if (result.Outcome == RunEndReason.Victory) clears++;
            stagesCleared += result.StagesCleared;
            Program.InvestMetaSouls(meta);
        }
        return new StyleStat(bot.Name, runs, clears, stagesCleared / (double)runs);
    }

    /// <summary>플레이 성향별 지표. (1) 어떤 성향도 30스테를 너무 쉽게 깨면 안 된다(styleClearRate 상한) —
    /// 한 성향이 압도적이면 다른 선택이 의미 없어진다. (2) 가장 약한 성향의 평균 클리어 스테이지가 가장 강한 성향의
    /// 일정 비율 이상이어야 한다(styleStageRatio 하한) — 특정 플레이 방식이 막혀 있지 않은지 본다.
    /// 균형형 봇 값은 이미 잰 기본 측정을 재사용하고, 나머지 성향 봇은 병렬로 새로 잰다.</summary>
    private static void EvaluatePlaystyles(JsonElement targets, Baseline primary, int runs, int seed, List<MetricResult> results)
    {
        if (!targets.TryGetProperty("playstyles", out var t) || !t.TryGetProperty("bots", out var botsEl)) return;

        var names = botsEl.EnumerateArray().Select(e => e.GetString() ?? "").Where(n => n.Length > 0 && n != "Balanced").Distinct().ToList();
        var measured = new ConcurrentDictionary<string, StyleStat>();
        Parallel.ForEach(names, name => measured[name] = MeasureStyle(BotCatalog.Create(name), runs, seed));

        var stats = new List<StyleStat>
        {
            new("Balanced", primary.Runs, primary.Clears, primary.TotalStagesCleared / (double)Math.Max(primary.Runs, 1)),
        };
        stats.AddRange(names.Select(n => measured[n]));

        double? maxClear = Num(t, "maxClearRate"), minRatio = Num(t, "minWeakToStrongStageRatio");
        const string group = "플레이 성향별";

        if (maxClear is not null)
            foreach (var st in stats)
                results.Add(Judge(group, $"styleClearRate.{st.Bot}", st.Clears / (double)st.Runs, null, maxClear, st.Runs, 1,
                    $"평균 {st.AvgStagesCleared:F1}스테 클리어", false));

        if (minRatio is not null && stats.Count >= 2)
        {
            double strong = stats.Max(x => x.AvgStagesCleared), weak = stats.Min(x => x.AvgStagesCleared);
            string detail = string.Join(" / ", stats.OrderByDescending(x => x.AvgStagesCleared).Select(x => $"{x.Bot} {x.AvgStagesCleared:F1}"));
            results.Add(Judge(group, "styleStageRatio", strong <= 0 ? null : weak / strong, minRatio, null, runs, 1, detail, false));
        }
    }

    /// <summary>실력 단계별 목표. 위의 스테이지 승률 목표는 "평균 플레이어"(Balanced 봇) 기준이고, 여기서는 "숙련자"(Expert 봇)가
    /// 후반을 얼마나 깨는지를 따로 본다: 29스테이지까지 도달하는 비율(reachStage)과 마지막 스테이지(finalStage)의 조건부 승률.
    /// 한쪽 실력에 난이도를 맞추면 다른 쪽이 너무 쉬워지거나 어려워지는 문제를 막기 위한 장치다.</summary>
    private static void EvaluateSkillTiers(JsonElement targets, int runs, int seed, List<MetricResult> results)
    {
        if (!targets.TryGetProperty("skillTiers", out var tiers) || tiers.ValueKind != JsonValueKind.Object) return;

        foreach (var tier in tiers.EnumerateObject())
        {
            if (tier.Name.StartsWith('_') || tier.Value.ValueKind != JsonValueKind.Object) continue;
            var t = tier.Value;
            string botName = t.TryGetProperty("bot", out var bn) ? bn.GetString() ?? tier.Name : tier.Name;
            int reachStage = t.TryGetProperty("reachStage", out var rs) ? rs.GetInt32() : 29;
            int finalStage = t.TryGetProperty("finalStage", out var fs) ? fs.GetInt32() : 30;

            var bot = BotCatalog.Create(botName);
            var rng = new Random(seed);
            var meta = new MetaProgression();
            var weapons = new WeaponTierMetrics();
            var attempts = new StageAttemptCollector();
            int reached = 0, finalAttempts = 0, finalWins = 0;
            for (int i = 0; i < runs; i++)
            {
                var r = Program.SimulateOneRun(bot, i, rng, meta, weapons, attempts);
                if (r.StagesCleared >= reachStage) reached++;
                if (r.StagesCleared >= finalStage - 1) finalAttempts++;
                if (r.StagesCleared >= finalStage) finalWins++;
                Program.InvestMetaSouls(meta);
            }

            string group = $"숙련자 ({botName})";
            if (t.TryGetProperty("reach", out var reach))
                results.Add(Judge(group, $"tier.{tier.Name}.reach{reachStage}", reached / (double)runs, Num(reach, "min"), Num(reach, "max"), runs, 1,
                    $"{reached}/{runs}", false));
            if (t.TryGetProperty("finalWinRate", out var fw))
                results.Add(Judge(group, $"tier.{tier.Name}.stage{finalStage}WinRate",
                    finalAttempts == 0 ? null : finalWins / (double)finalAttempts, Num(fw, "min"), Num(fw, "max"), finalAttempts, (int)MinStageSample,
                    $"{finalWins}/{finalAttempts}", false));
        }
    }

    private static void EvaluateRelicBalance(JsonElement targets, int relicRuns, int seed, List<MetricResult> results)
    {
        if (!targets.TryGetProperty("relicBalance", out var t)) return;
        double maxRatio = Num(t, "maxRunClearRatioWithinChoice") ?? 2.0;

        var jobs = new List<(int Stage, BossRelic Relic)>();
        var stages = new List<int>();
        foreach (int stage in new[] { 5, 10, 15, 20, 25, 30 })
        {
            var cands = BossRelicCatalog.CandidatesFor(stage);
            if (cands.Count < 2) continue;
            stages.Add(stage);
            foreach (var r in cands) jobs.Add((stage, r));
        }

        var rates = new ConcurrentDictionary<(int, string), double>();
        Parallel.ForEach(jobs, job =>
        {
            int jobSeed = HashSeed(seed, job.Stage, job.Relic.Id);
            rates[(job.Stage, job.Relic.Id)] = RelicBatchClearRate(job.Stage, job.Relic.Id, relicRuns, jobSeed);
        });

        foreach (int stage in stages)
        {
            var cands = BossRelicCatalog.CandidatesFor(stage);
            var pairs = cands.Select(c => (c.Id, Rate: rates[(stage, c.Id)])).ToList();
            double hi = pairs.Max(p => p.Rate), lo = pairs.Min(p => p.Rate);
            string detail = string.Join(" / ", pairs.OrderByDescending(p => p.Rate).Select(p => $"{p.Id} {p.Rate:P2}"));

            // 완주 횟수가 너무 적으면 비율이 잡음이라 판정 보류.
            if (hi * relicRuns < 10)
            {
                results.Add(Judge("유물 격차", $"relicRatio.stage{stage}", null, null, maxRatio, relicRuns, 1, detail + " (완주 표본 부족)", false));
                continue;
            }

            double ratio = lo <= 0 ? 999 : hi / lo;
            results.Add(Judge("유물 격차", $"relicRatio.stage{stage}", ratio, null, maxRatio, relicRuns, 1, detail, false));
        }
    }

    private static int HashSeed(int seed, int stage, string relicId)
    {
        unchecked
        {
            int h = seed * 31 + stage;
            foreach (char c in relicId) h = h * 31 + c;
            return h;
        }
    }

    // ───────────────────────── 공통 ─────────────────────────

    private static MetricResult Judge(string group, string metric, double? value, double? min, double? max, int sample, int minSample, string? detail, bool draft)
    {
        if (value is null || sample < minSample)
            return new MetricResult(group, metric, value, min, max, "INSUFFICIENT", 0, sample, detail, draft);

        if (min is { } lo && value < lo)
            return new MetricResult(group, metric, value, min, max, "LOW", value.Value - lo, sample, detail, draft);

        if (max is { } hi && value > hi)
            return new MetricResult(group, metric, value, min, max, "HIGH", value.Value - hi, sample, detail, draft);

        return new MetricResult(group, metric, value, min, max, "OK", 0, sample, detail, draft);
    }

    private static double? Num(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    internal static string ResolveTargetsPath(string? explicitPath)
    {
        if (explicitPath is not null)
        {
            if (!File.Exists(explicitPath)) throw new FileNotFoundException($"목표 지표 파일을 찾을 수 없습니다: {explicitPath}");
            return Path.GetFullPath(explicitPath);
        }

        string baseDirCopy = Path.Combine(AppContext.BaseDirectory, "BalanceTargets.json");
        if (File.Exists(baseDirCopy)) return baseDirCopy;

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "DungeonVM.Simulator", "BalanceTargets.json");
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException("BalanceTargets.json을 찾을 수 없습니다. --targets <경로>로 지정하세요.");
    }

    // ───────────────────────── 출력 ─────────────────────────

    private static string Fmt(string metric, double? v)
    {
        if (v is null) return "-";
        bool pct = metric.StartsWith("stageWinRate", StringComparison.Ordinal)
            || metric.StartsWith("weaponTypeShare", StringComparison.Ordinal)
            || metric.StartsWith("styleClearRate", StringComparison.Ordinal)
            || metric.StartsWith("tier.", StringComparison.Ordinal)
            || metric == "runClearRate";
        return pct ? $"{v:P1}" : $"{v:F2}";
    }

    private static void PrintReport(List<MetricResult> results)
    {
        Console.WriteLine();
        Console.WriteLine("=== 밸런스 평가 결과 (목표 대비) ===");
        foreach (var group in results.GroupBy(r => r.Group))
        {
            Console.WriteLine($"\n[{group.Key}]");
            foreach (var r in group)
            {
                string range = $"{(r.Min is null ? "" : Fmt(r.Metric, r.Min))}~{(r.Max is null ? "" : Fmt(r.Metric, r.Max))}";
                string mark = r.Status switch { "OK" => "  OK  ", "LOW" => " 미달 ", "HIGH" => " 초과 ", _ => " 보류 " };
                string draft = r.Draft ? " (초안)" : "";
                string detail = r.Detail is null ? "" : $"  [{r.Detail}]";
                Console.WriteLine($" {mark} {r.Metric,-34} 측정 {Fmt(r.Metric, r.Value),8}  목표 {range,-14}{draft}{detail}");
            }
        }

        int ok = results.Count(r => r.Status == "OK");
        int dev = results.Count(r => r.Status is "LOW" or "HIGH");
        int insufficient = results.Count(r => r.Status == "INSUFFICIENT");
        Console.WriteLine($"\n요약: 전체 {results.Count}건 — 목표 달성 {ok}, 벗어남 {dev}, 표본 부족(보류) {insufficient}");
        Console.WriteLine($"튜닝 점수(낮을수록 좋음, 초안/유물 격차 제외): {Score(results):F3}");
    }

    private static void WriteJson(List<MetricResult> results, int runs, int relicRuns, bool skipRelics, int seed, string targetsFile, string? outPath)
    {
        var payload = new
        {
            generatedAtUtc = DateTime.UtcNow,
            targetsFile,
            runs,
            relicRunsPerChoice = skipRelics ? 0 : relicRuns,
            seed,
            summary = new
            {
                total = results.Count,
                ok = results.Count(r => r.Status == "OK"),
                deviating = results.Count(r => r.Status is "LOW" or "HIGH"),
                insufficient = results.Count(r => r.Status == "INSUFFICIENT"),
            },
            results,
        };

        string path = outPath ?? Path.Combine(AppContext.BaseDirectory, "balance_eval.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }));
        Console.WriteLine($"평가 결과를 {path}에 저장했습니다.");
    }
}
