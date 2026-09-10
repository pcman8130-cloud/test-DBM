using System.Diagnostics;
using System.Text.Json;
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
/// "왜 유저가 고티어 대신 2티어 무기에 안주하는가"를 무기 티어 채택률로 검증한다.
/// </summary>
internal static class Program
{
    private const double TickSeconds = 0.25;
    private const double MaxSecondsPerStage = 60;
    private const int StartingGold = 100;

    private static async Task<int> Main(string[] args)
    {
        int runsPerBot = args.Length > 0 && int.TryParse(args[0], out var n) ? n : 1000;

        IBot[] bots = { new GreedyMergerBot(), new SaverUpgraderBot(), new BalancedOptimizerBot() };
        var metaByBot = bots.ToDictionary(b => b.Name, _ => new MetaProgression());
        var metricsByBot = bots.ToDictionary(b => b.Name, _ => new WeaponTierMetrics());
        var collector = new RunLogCollector();

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

        foreach (var bot in bots)
        {
            var rng = new Random(HashCode.Combine(bot.Name, 42));
            var meta = metaByBot[bot.Name];
            var metrics = metricsByBot[bot.Name];

            for (int i = 0; i < runsPerBot; i++)
            {
                var result = SimulateOneRun(bot, i, rng, meta, metrics);
                collector.Add(result);

                await fileSink.LogRunAsync(result);
                if (supabase.IsConfigured)
                    await supabase.LogRunAsync(result);

                InvestMetaSouls(meta);
                totalRuns++;

                if (totalRuns % 500 == 0)
                    Console.WriteLine($"  진행: {totalRuns}/{totalTarget} ({sw.Elapsed.TotalSeconds:F1}s)");
            }
        }

        sw.Stop();
        Console.WriteLine($"\n총 {totalRuns}회 실행 완료 ({sw.Elapsed.TotalSeconds:F1}s)\n");

        PrintReport(bots, collector, metricsByBot);
        await RunLlmBalancingAsync(metricsByBot);

        return 0;
    }

    private static RunResult SimulateOneRun(IBot bot, int runIndex, Random rng, MetaProgression meta, WeaponTierMetrics metrics)
    {
        var currency = new CurrencyManager();
        var inventory = new InventoryManager();
        var party = new List<Character>
        {
            new("Hero1", meta.BaseHealthBonus),
            new("Hero2", meta.BaseHealthBonus),
        };
        var stageLoop = new StageLoop(currency, inventory, party);
        var ctx = new BotContext(stageLoop, rng, meta);

        currency.Add(CurrencyType.Gold, StartingGold);

        if (rng.NextDouble() < meta.FirstRollTierBoostChance)
            stageLoop.Machine.NextRollGuaranteedTier2 = true;

        while (!stageLoop.IsRunComplete)
        {
            bot.OnMaintenancePhase(ctx);

            var monsters = stageLoop.BeginStageCombat(rng);
            var battle = new BattleField(party, monsters, stageLoop.Machine, rng);

            var outcome = RunEndReason.InProgress;
            double elapsed = 0;

            while (outcome == RunEndReason.InProgress && elapsed < MaxSecondsPerStage)
            {
                bot.OnCombatTick(ctx);
                outcome = battle.Tick(TickSeconds, meta.RetireSpeedMultiplier);
                elapsed += TickSeconds;
            }

            if (outcome == RunEndReason.Victory)
            {
                stageLoop.CompleteStageVictory();
                continue;
            }

            // 제한 시간 초과 = 사실상 돌파 불가로 간주하고 패배 처리
            if (outcome == RunEndReason.InProgress)
                outcome = RunEndReason.PartyWiped;

            return FinishRun(bot, runIndex, outcome, stageLoop, currency, meta, metrics);
        }

        return FinishRun(bot, runIndex, RunEndReason.Victory, stageLoop, currency, meta, metrics);
    }

    private static RunResult FinishRun(
        IBot bot, int runIndex, RunEndReason outcome, StageLoop stageLoop,
        CurrencyManager currency, MetaProgression meta, WeaponTierMetrics metrics)
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

        return new RunResult(bot.Name, runIndex, outcome, stagesCleared, currency.Gold, currency.Gems, currency.Souls, weapons, armors);
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

            foreach (var (reason, count) in collector.OutcomeBreakdown(bot.Name))
                Console.WriteLine($"  종료 사유 {reason}: {count}건");

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
