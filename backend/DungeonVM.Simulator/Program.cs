using System.Diagnostics;
using System.Text.Json;
using DungeonVM.Core.Balance;
using DungeonVM.Core.Combat;
using DungeonVM.Core.Enums;
using DungeonVM.Core.Inventory;
using DungeonVM.Core.Models;
using DungeonVM.Core.Systems;
using DungeonVM.LLM;
using DungeonVM.Simulator.Bots;
using DungeonVM.Simulator.Metrics;

namespace DungeonVM.Simulator;

/// <summary>
/// 던전 자판기 헤드리스 배치 시뮬레이터. 성향이 다른 가상 봇 3종을 각 1,000회(총 3,000회) 완주시켜
/// "고레벨 무기의 DPS/스킬이 강력함에도 불구하고, 왜 유저(봇)는 머지 그리드 병목·골드 해금 비용·
/// 소켓 룬 소멸 패널티로 인해 중간 레벨(5~10레벨)에 안주하는가"를 레벨 분포/그리드 병목/룬 회피 지표로 검증한다.
/// --balance &lt;path&gt; (또는 DUNGEONVM_BALANCE_JSON 환경변수)로 밸런스 JSON을 덮어써서
/// 엑셀→JSON 파이프라인 산출물이나 대시보드가 조정한 값으로 재시뮬레이션할 수 있다.
/// </summary>
internal static class Program
{
    private const double TickSeconds = 0.25;
    private const double MaxSecondsPerStage = 60;
    private const int StartingGold = 100;

    private static async Task<int> Main(string[] args)
    {
        var parsed = ParseArgs(args);

        if (parsed.BalancePath is not null)
        {
            BalanceProvider.LoadFromFile(parsed.BalancePath);
            Console.WriteLine($"밸런스 오버라이드 로드: {parsed.BalancePath}");
        }
        else
        {
            Console.WriteLine("밸런스: DungeonVM.Core 기본값(임베디드 DefaultBalance.json) 사용");
        }

        int runsPerBot = parsed.RunsPerBot;

        IBot[] bots = { new SpaceExpansionBot(), new VendingRushBot(), new MidTierCampBot() };
        var metaByBot = bots.ToDictionary(b => b.Name, _ => new MetaProgression());
        var metricsByBot = bots.ToDictionary(b => b.Name, _ => new WeaponTierMetrics());
        var collector = new RunLogCollector();
        var stageAttempts = new StageAttemptCollector();

        using var fileSink = new FileRunLogSink(Path.Combine(AppContext.BaseDirectory, "run_logs.jsonl"));
        var supabase = new SupabaseLogger();

        Console.WriteLine($"던전 자판기 헤드리스 시뮬레이터 — 봇 {bots.Length}종 x {runsPerBot}회 = 총 {bots.Length * runsPerBot}회 배치 실행");
        Console.WriteLine(supabase.IsConfigured
            ? "Supabase 로깅: 활성화"
            : "Supabase 로깅: 비활성화 (SUPABASE_URL / SUPABASE_SERVICE_KEY 없음) — 로컬 run_logs.jsonl만 기록");
        Console.WriteLine();

        var sw = Stopwatch.StartNew();
        int totalRuns = 0;
        int totalTarget = bots.Length * runsPerBot;
        var completedRunsByBot = bots.ToDictionary(b => b.Name, _ => 0);

        foreach (var bot in bots)
        {
            var rng = new Random(HashCode.Combine(bot.Name, 42));
            var meta = metaByBot[bot.Name];
            var metrics = metricsByBot[bot.Name];

            for (int i = 0; i < runsPerBot; i++)
            {
                var result = SimulateOneRun(bot, i, rng, meta, metrics, stageAttempts);
                collector.Add(result);

                await fileSink.LogRunAsync(result);
                if (supabase.IsConfigured)
                    await supabase.LogRunAsync(result);

                InvestMetaSouls(meta);
                totalRuns++;
                completedRunsByBot[bot.Name]++;

                if (totalRuns % 500 == 0)
                    Console.WriteLine($"  진행: {totalRuns}/{totalTarget} ({sw.Elapsed.TotalSeconds:F1}s)");

                if (parsed.ProgressPath is not null && totalRuns % 10 == 0)
                    WriteProgress(parsed.ProgressPath, "running", bots, runsPerBot, totalRuns, totalTarget, bot.Name, completedRunsByBot, collector, stageAttempts, sw.Elapsed.TotalSeconds);
            }
        }

        sw.Stop();
        Console.WriteLine($"\n총 {totalRuns}회 실행 완료 ({sw.Elapsed.TotalSeconds:F1}s)\n");

        if (parsed.ProgressPath is not null)
            WriteProgress(parsed.ProgressPath, "done", bots, runsPerBot, totalRuns, totalTarget, bots[^1].Name, completedRunsByBot, collector, stageAttempts, sw.Elapsed.TotalSeconds);

        PrintReport(bots, collector, metricsByBot);
        WriteSummaryJson(bots, collector, metricsByBot, stageAttempts, parsed.SummaryPath);

        if (!parsed.SkipLlm)
            await RunLlmBalancingAsync(metricsByBot);

        return 0;
    }

    private sealed record ParsedArgs(int RunsPerBot, string? BalancePath, string? SummaryPath, string? ProgressPath, bool SkipLlm);

    /// <summary>
    /// "[숫자] [--balance &lt;path&gt;] [--summary &lt;path&gt;] [--progress &lt;path&gt;] [--skip-llm]" 형태를 파싱한다.
    /// --balance가 없으면 DUNGEONVM_BALANCE_JSON 환경변수를 확인한다.
    /// --summary는 summary.json을 저장할 경로를 직접 지정한다(대시보드 등이 빌드 출력 폴더 경로를 추측하지 않아도 되게).
    /// --progress는 실행 도중(10런마다) 진행 상황을 덮어쓰는 JSON 경로 — 웹 UI가 폴링해서 실시간 진행률을 보여줄 때 사용.
    /// --skip-llm은 LLM 밸런싱 모듈(네트워크 호출) 실행을 건너뛴다(대시보드처럼 반복 실행 시 API 비용/지연을 피하기 위함).
    /// </summary>
    private static ParsedArgs ParseArgs(string[] args)
    {
        int runsPerBot = 1000;
        string? balancePath = null;
        string? summaryPath = null;
        string? progressPath = null;
        bool skipLlm = false;
        var positional = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--balance":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--balance 옵션 뒤에는 JSON 파일 경로가 와야 합니다.");
                    balancePath = args[++i];
                    break;
                case "--summary":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--summary 옵션 뒤에는 저장할 JSON 파일 경로가 와야 합니다.");
                    summaryPath = args[++i];
                    break;
                case "--progress":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--progress 옵션 뒤에는 저장할 JSON 파일 경로가 와야 합니다.");
                    progressPath = args[++i];
                    break;
                case "--skip-llm":
                    skipLlm = true;
                    break;
                default:
                    if (args[i].StartsWith("--balance=", StringComparison.Ordinal))
                        balancePath = args[i]["--balance=".Length..];
                    else if (args[i].StartsWith("--summary=", StringComparison.Ordinal))
                        summaryPath = args[i]["--summary=".Length..];
                    else if (args[i].StartsWith("--progress=", StringComparison.Ordinal))
                        progressPath = args[i]["--progress=".Length..];
                    else
                        positional.Add(args[i]);
                    break;
            }
        }

        if (positional.Count > 0 && int.TryParse(positional[0], out var n))
            runsPerBot = n;

        balancePath ??= Environment.GetEnvironmentVariable("DUNGEONVM_BALANCE_JSON");
        return new ParsedArgs(runsPerBot, balancePath, summaryPath, progressPath, skipLlm);
    }

    /// <summary>실행 도중 진행 상황을 파일에 덮어쓴다. 웹 UI가 이 파일을 주기적으로 폴링해서 진행률/실시간 지표를 보여준다.</summary>
    private static void WriteProgress(
        string path, string status, IBot[] bots, int runsPerBot, int totalRuns, int totalTarget, string currentBotName,
        Dictionary<string, int> completedRunsByBot, RunLogCollector collector, StageAttemptCollector stageAttempts, double elapsedSeconds)
    {
        var perBot = bots.Select(b => new
        {
            botName = b.Name,
            completedRuns = completedRunsByBot[b.Name],
            runsPerBot,
            winRate = collector.WinRate(b.Name),
            averageStagesCleared = collector.AverageStagesCleared(b.Name),
            battlesFought = stageAttempts.TotalAttempts(b.Name),
        }).ToList();

        var progress = new
        {
            status,
            currentBotName,
            completedRuns = totalRuns,
            totalRunsPlanned = totalTarget,
            percent = totalTarget == 0 ? 0 : totalRuns / (double)totalTarget,
            elapsedSeconds,
            battlesFoughtTotal = stageAttempts.TotalAttempts(),
            battlesWonTotal = stageAttempts.TotalWins(),
            overallWinRateSoFar = stageAttempts.OverallWinRate(),
            overallAvgClearSecondsSoFar = stageAttempts.OverallAvgClearSeconds(),
            perBot,
        };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(progress));
            File.Copy(tmp, path, overwrite: true);
            File.Delete(tmp);
        }
        catch (IOException)
        {
            // 폴링 중인 리더와 파일 쓰기가 겹쳐 실패해도 다음 주기(10런 뒤)에 다시 쓰므로 무시한다.
        }
    }

    private static RunResult SimulateOneRun(IBot bot, int runIndex, Random rng, MetaProgression meta, WeaponTierMetrics metrics, StageAttemptCollector stageAttempts)
    {
        var currency = new CurrencyManager();
        var inventory = new InventoryManager();
        int startingSlots = BalanceProvider.Current.CharacterSlots.StartingSlots;
        var party = Enumerable.Range(1, startingSlots)
            .Select(i => new Character($"Hero{i}", meta.BaseHealthBonus))
            .ToList();
        var stageLoop = new StageLoop(currency, inventory, party);
        var ctx = new BotContext(stageLoop, rng, meta);

        currency.Add(CurrencyType.Gold, StartingGold);

        if (rng.NextDouble() < meta.FirstRollTierBoostChance)
            stageLoop.Machine.NextRollGuaranteedTier2 = true;

        while (!stageLoop.IsRunComplete)
        {
            bot.OnMaintenancePhase(ctx);

            var monsters = stageLoop.BeginStageCombat(rng);
            var battle = new BattleField(party, monsters, rng);

            var outcome = RunEndReason.InProgress;
            double elapsed = 0;

            while (outcome == RunEndReason.InProgress && elapsed < MaxSecondsPerStage)
            {
                bot.OnCombatTick(ctx);
                outcome = battle.Tick(TickSeconds, meta.RetireSpeedMultiplier + stageLoop.RelicRetireSpeedBonus);
                elapsed += TickSeconds;
            }

            // 제한 시간 초과 = 사실상 돌파 불가로 간주하고 패배 처리
            if (outcome == RunEndReason.InProgress)
                outcome = RunEndReason.PartyWiped;

            double remainingHpRatio = party.Average(c => c.CurrentHealth / c.MaxHealth);
            stageAttempts.Add(new StageAttempt(bot.Name, stageLoop.CurrentStage, outcome, elapsed, remainingHpRatio));

            if (outcome == RunEndReason.Victory)
            {
                var rewardChoice = stageLoop.CompleteStageVictory(rng);
                int selected = bot.ChooseStageReward(ctx, rewardChoice);
                stageLoop.ResolveStageRewardChoice(rewardChoice, selected, rng);
                continue;
            }

            return FinishRun(bot, runIndex, outcome, stageLoop, currency, meta, metrics, ctx);
        }

        return FinishRun(bot, runIndex, RunEndReason.Victory, stageLoop, currency, meta, metrics, ctx);
    }

    private static RunResult FinishRun(
        IBot bot, int runIndex, RunEndReason outcome, StageLoop stageLoop,
        CurrencyManager currency, MetaProgression meta, WeaponTierMetrics metrics, BotContext ctx)
    {
        meta.BankSouls(stageLoop.SoulsEarnedThisRun);
        metrics.RecordFinalEquip(stageLoop.Party);

        int stagesCleared = Math.Clamp(stageLoop.CurrentStage - 1, 0, StageLoop.MaxStage);

        var weapons = stageLoop.Party
            .Where(c => c.EquippedWeapon is not null)
            .Select(c => $"{c.EquippedWeapon!.Type}_T{c.EquippedWeapon!.Tier}")
            .ToList();

        var armors = stageLoop.Party
            .Where(c => c.EquippedArmor is not null)
            .Select(c => $"{c.EquippedArmor!.Type}_{c.EquippedArmor!.Rarity}")
            .ToList();

        return new RunResult(
            bot.Name, runIndex, outcome, stagesCleared, currency.Gold, currency.Souls,
            weapons, armors, ctx.GridBottleneckSells, ctx.RuneAvoidanceSkips);
    }

    private static void InvestMetaSouls(MetaProgression meta)
    {
        bool progressed = true;
        while (progressed)
        {
            bool a = meta.TryUpgradeRetireTimeReduction();
            bool b = meta.TryUpgradeFirstRollTierBoost();
            bool c = meta.TryUpgradeBaseStat();
            progressed = a || b || c;
        }
    }

    private static void PrintReport(IBot[] bots, RunLogCollector collector, Dictionary<string, WeaponTierMetrics> metricsByBot)
    {
        Console.WriteLine("=== 봇별 런 요약 ===");
        foreach (var bot in bots)
        {
            Console.WriteLine($"[{bot.Name}]");
            Console.WriteLine($"  승률(30스테이지 완주): {collector.WinRate(bot.Name):P1}");
            Console.WriteLine($"  평균 도달 스테이지: {collector.AverageStagesCleared(bot.Name):F1} / {StageLoop.MaxStage}");
            Console.WriteLine($"  평균 최종 골드: {collector.AverageFinalGold(bot.Name):F0}");
            Console.WriteLine($"  평균 그리드 병목 강제판매: {collector.AverageGridBottleneckSells(bot.Name):F1}회/런");
            Console.WriteLine($"  평균 룬 보존 위한 머지 회피: {collector.AverageRuneAvoidanceSkips(bot.Name):F1}회/런");

            foreach (var (reason, count) in collector.OutcomeBreakdown(bot.Name))
                Console.WriteLine($"  종료 사유 {reason}: {count}건");

            Console.WriteLine("  레벨 구간 분포(런 종료 시점 장착 무기 기준):");
            foreach (var (bucket, rate) in collector.LevelBucketDistribution(bot.Name))
                Console.WriteLine($"    Lv.{bucket}: {rate:P1}");

            Console.WriteLine();
        }

        Console.WriteLine("=== 무기 티어 채택률 (런 종료 시점 장착 스냅샷 기준) ===");
        foreach (var bot in bots)
        {
            var metrics = metricsByBot[bot.Name];
            Console.WriteLine($"[{bot.Name}] (표본 {metrics.TotalWeaponObservations}건)");

            foreach (WeaponType type in Enum.GetValues(typeof(WeaponType)))
            {
                for (int tier = 1; tier <= WeaponCatalog.MaxTier; tier++)
                {
                    double rate = metrics.WeaponAdoptionRate(type, tier);
                    if (rate <= 0) continue;
                    Console.WriteLine($"  {type,-8} T{tier}: {rate:P1}");
                }
            }
            Console.WriteLine();
        }

        Console.WriteLine("=== 방어구 등급 채택률 (런 종료 시점 장착 스냅샷 기준) ===");
        foreach (var bot in bots)
        {
            var metrics = metricsByBot[bot.Name];
            Console.WriteLine($"[{bot.Name}] (표본 {metrics.TotalArmorObservations}건)");

            foreach (ArmorType type in Enum.GetValues(typeof(ArmorType)))
            {
                foreach (ArmorRarity rarity in Enum.GetValues(typeof(ArmorRarity)))
                {
                    double rate = metrics.ArmorAdoptionRate(type, rarity);
                    if (rate <= 0) continue;
                    Console.WriteLine($"  {type,-6} {rarity,-9}: {rate:P1}");
                }
            }
            Console.WriteLine();
        }
    }

    /// <summary>
    /// 콘솔 리포트와 동일한 수치를 summary.json으로 저장한다. 실시간 대시보드(B)가 콘솔 출력을 파싱하지 않고
    /// 이 파일 하나만 읽어서 밸런스 파라미터 변경 → 재시뮬레이션 → 그래프 갱신 루프를 구성할 수 있게 하기 위함.
    /// </summary>
    private static void WriteSummaryJson(IBot[] bots, RunLogCollector collector, Dictionary<string, WeaponTierMetrics> metricsByBot, StageAttemptCollector stageAttempts, string? summaryPath)
    {
        var botSummaries = bots.Select(bot =>
        {
            var metrics = metricsByBot[bot.Name];
            var stageBreakdown = stageAttempts.ByStage(bot.Name)
                .Select(s => new
                {
                    stage = s.Stage,
                    attempts = s.Attempts,
                    wins = s.Wins,
                    winRate = s.WinRate,
                    avgClearSeconds = s.AvgClearSeconds,
                    avgRemainingHpRatio = s.AvgRemainingHpRatio,
                })
                .ToList();

            var weaponAdoption = Enum.GetValues(typeof(WeaponType)).Cast<WeaponType>()
                .SelectMany(type => Enumerable.Range(1, WeaponCatalog.MaxTier)
                    .Select(tier => (type, tier, rate: metrics.WeaponAdoptionRate(type, tier))))
                .Where(x => x.rate > 0)
                .Select(x => new { weaponType = x.type.ToString(), tier = x.tier, adoptionRate = x.rate })
                .ToList();

            var armorAdoption = Enum.GetValues(typeof(ArmorType)).Cast<ArmorType>()
                .SelectMany(type => Enum.GetValues(typeof(ArmorRarity)).Cast<ArmorRarity>()
                    .Select(rarity => (type, rarity, rate: metrics.ArmorAdoptionRate(type, rarity))))
                .Where(x => x.rate > 0)
                .Select(x => new { armorType = x.type.ToString(), rarity = x.rarity.ToString(), adoptionRate = x.rate })
                .ToList();

            return new
            {
                botName = bot.Name,
                winRate = collector.WinRate(bot.Name),
                averageStagesCleared = collector.AverageStagesCleared(bot.Name),
                averageFinalGold = collector.AverageFinalGold(bot.Name),
                averageGridBottleneckSells = collector.AverageGridBottleneckSells(bot.Name),
                averageRuneAvoidanceSkips = collector.AverageRuneAvoidanceSkips(bot.Name),
                levelBucketDistribution = collector.LevelBucketDistribution(bot.Name),
                outcomeBreakdown = collector.OutcomeBreakdown(bot.Name).ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                weaponAdoption,
                armorAdoption,
                stageBreakdown,
            };
        }).ToList();

        var summary = new
        {
            generatedAtUtc = DateTime.UtcNow,
            maxStage = StageLoop.MaxStage,
            bots = botSummaries,
        };

        string path = summaryPath ?? Path.Combine(AppContext.BaseDirectory, "summary.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"요약 결과를 {path}에 저장했습니다.\n");
    }

    private static async Task RunLlmBalancingAsync(Dictionary<string, WeaponTierMetrics> metricsByBot)
    {
        var combined = new WeaponTierMetrics();
        foreach (var metrics in metricsByBot.Values)
            combined.MergeFrom(metrics);

        var lowAdoptionItems = new List<LowAdoptionItem>();

        foreach (var (key, count) in combined.WeaponEquipCounts)
        {
            double rate = combined.WeaponAdoptionRate(key.Type, key.Tier);
            if (rate >= RecipeOptimizer.AdoptionThreshold) continue;

            string statsJson = JsonSerializer.Serialize(new
            {
                damage = WeaponCatalog.DamageAtTier(key.Type, key.Tier),
                attacksPerSecond = WeaponCatalog.Get(key.Type).AttacksPerSecond,
                row = WeaponCatalog.RowOf(key.Type).ToString(),
            });

            lowAdoptionItems.Add(new LowAdoptionItem($"Weapon.{key.Type}.T{key.Tier}", "Weapon", rate, combined.TotalWeaponObservations, statsJson));
        }

        foreach (var (key, count) in combined.ArmorEquipCounts)
        {
            double rate = combined.ArmorAdoptionRate(key.Type, key.Rarity);
            if (rate >= RecipeOptimizer.AdoptionThreshold) continue;

            string statsJson = JsonSerializer.Serialize(new { rarity = key.Rarity.ToString() });
            lowAdoptionItems.Add(new LowAdoptionItem($"Armor.{key.Type}.{key.Rarity}", "Armor", rate, combined.TotalArmorObservations, statsJson));
        }

        Console.WriteLine($"=== LLM 밸런싱 모듈: 채택률 {RecipeOptimizer.AdoptionThreshold:P0} 미만 아이템 {lowAdoptionItems.Count}건 감지 ===");

        if (lowAdoptionItems.Count == 0)
        {
            Console.WriteLine("  감지된 저채택 아이템이 없습니다.");
            return;
        }

        foreach (var item in lowAdoptionItems)
            Console.WriteLine($"  - {item.ItemId}: {item.AdoptionRate:P2} (n={item.SampleSize})");

        ILlmClient client = new AnthropicClient();
        if (!client.IsConfigured)
            client = new OpenAiClient();

        var optimizer = new RecipeOptimizer(client);
        optimizer.OnLog += msg => Console.WriteLine($"  {msg}");

        var suggestions = await optimizer.OptimizeAsync(lowAdoptionItems);
        if (suggestions.Count == 0) return;

        string outPath = Path.Combine(AppContext.BaseDirectory, "balance_suggestions.json");
        await RecipeOptimizer.SaveSuggestionsAsync(outPath, suggestions);
        Console.WriteLine($"  제안 {suggestions.Count}건을 {outPath}에 저장했습니다.");
    }
}
