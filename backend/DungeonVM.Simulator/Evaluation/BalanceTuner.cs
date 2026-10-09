using System.Globalization;
using System.Text.Json;
using DungeonVM.Core.Balance;
using DungeonVM.LLM;
using DungeonVM.LLM.Tuning;

namespace DungeonVM.Simulator.Evaluation;

/// <summary>
/// 밸런스 튜닝 루프: 평가(목표 대비 편차) → 제안자(LLM)가 화이트리스트 안에서 수치 변경안 제안 → 검증(허용 범위/경로) →
/// 적용 후 재시뮬레이션 → 점수가 개선되면 채택, 아니면 되돌림 → 반복. 마지막에 다른 시드로 원본/튜닝본을 다시 측정해
/// 특정 시드에 과적합되지 않았는지 확인한다.
/// </summary>
public static class BalanceTuner
{
    private const double AcceptEpsilon = 1e-4;

    public sealed record Options(
        int Runs, int Iterations, int MaxChanges, bool MockLlm, int Seed, string? TargetsPath, string? OutDir);

    private sealed record RejectedChange(ParameterChange Change, string Reason);
    private sealed record IterationLog(
        int Iteration, string Status, IReadOnlyList<ParameterChange> Applied, IReadOnlyList<RejectedChange> Rejected,
        double ScoreBefore, double ScoreAfter, string Note);

    public static async Task<int> RunAsync(Options o)
    {
        ITuningProposer proposer;
        if (o.MockLlm)
        {
            proposer = new HeuristicTuningProposer();
        }
        else
        {
            var client = LlmClientFactory.CreateConfigured();
            if (client is null)
            {
                Console.Error.WriteLine("LLM API 키가 없습니다. ANTHROPIC_API_KEY, OPENAI_API_KEY 또는 GEMINI_API_KEY를 설정하거나, 루프 동작만 확인하려면 --mock-llm을 사용하세요.");
                return 2;
            }
            proposer = new LlmTuningProposer(client);
        }

        string targetsPath = BalanceEvaluator.ResolveTargetsPath(o.TargetsPath);
        using var targetsDoc = JsonDocument.Parse(File.ReadAllText(targetsPath));
        var targets = targetsDoc.RootElement;
        var whitelist = TunableWhitelist.Load(targets);
        if (whitelist.Count == 0)
        {
            Console.Error.WriteLine("BalanceTargets.json에 tunableParameters가 없습니다.");
            return 2;
        }

        Console.WriteLine($"밸런스 튜닝 — 제안자: {proposer.Name}");
        Console.WriteLine($"목표: {targetsPath} | 화이트리스트 {whitelist.Count}개 | 평가 {o.Runs}런(시드 {o.Seed}) | 최대 {o.Iterations}회 반복, 회당 변경 최대 {o.MaxChanges}개");

        var original = BalanceMutator.Clone(BalanceProvider.Current);
        var current = BalanceMutator.Clone(original);

        var results = BalanceEvaluator.EvaluateAll(targets, o.Runs, 0, true, o.Seed);
        double initialScore = BalanceEvaluator.Score(results);
        double score = initialScore;
        Console.WriteLine($"\n[초기] 점수 {Fmt(score)} | 벗어난 지표 {results.Count(BalanceEvaluator.IsScored)}개");

        var history = new List<TuningAttempt>();
        var logs = new List<IterationLog>();

        for (int it = 1; it <= o.Iterations; it++)
        {
            var deviations = results.Where(BalanceEvaluator.IsScored).Select(ToInfo).ToList();
            if (deviations.Count == 0)
            {
                Console.WriteLine("\n점수 대상 지표가 모두 목표 범위 안입니다. 반복을 종료합니다.");
                break;
            }

            var info = results.Where(r => r.Status is "LOW" or "HIGH" && !BalanceEvaluator.IsScored(r)).Select(ToInfo).ToList();
            var ctx = new TuningContext(deviations, info, whitelist.Describe(current), history, o.MaxChanges, BuildGameNotes(current));

            Console.WriteLine($"\n── 반복 {it}/{o.Iterations} ── 편차 {deviations.Count}건을 {proposer.Name}에 전달");

            IReadOnlyList<ParameterChange> proposed;
            try
            {
                proposed = await proposer.ProposeAsync(ctx);
            }
            catch (LlmAuthException ex)
            {
                Console.Error.WriteLine($"  {ex.Message}\n  키를 확인한 뒤 다시 실행하세요. 튜닝을 중단합니다(변경 사항 없음).");
                BalanceMutator.Install(original);
                return 3;
            }
            catch (Exception ex)
            {
                string note = $"제안 요청 실패: {ex.Message}";
                Console.WriteLine("  " + note);
                history.Add(new TuningAttempt(it, Array.Empty<ParameterChange>(), false, score, score, note));
                logs.Add(new IterationLog(it, "ERROR", Array.Empty<ParameterChange>(), Array.Empty<RejectedChange>(), score, score, note));
                continue;
            }

            var valid = new List<ParameterChange>();
            var rejected = new List<RejectedChange>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in proposed)
            {
                if (valid.Count >= o.MaxChanges) { rejected.Add(new RejectedChange(p, $"회당 최대 {o.MaxChanges}개 초과")); continue; }
                var (ok, infoText) = whitelist.Validate(p, current);
                if (!ok) { rejected.Add(new RejectedChange(p, infoText)); continue; }
                if (!seen.Add(infoText)) { rejected.Add(new RejectedChange(p, "같은 경로 중복 제안")); continue; }
                valid.Add(p with { Path = infoText });
            }

            Console.WriteLine($"  제안 {proposed.Count}건 → 검증 통과 {valid.Count}건, 거부 {rejected.Count}건");
            foreach (var r in rejected)
                Console.WriteLine($"    ✗ {r.Change.Path} → {r.Change.NewValue:G6}  ({r.Reason})");

            if (valid.Count == 0)
            {
                const string note = "유효한 제안 없음";
                history.Add(new TuningAttempt(it, Array.Empty<ParameterChange>(), false, score, score, note));
                logs.Add(new IterationLog(it, "NO_VALID_CHANGE", Array.Empty<ParameterChange>(), rejected, score, score, note));
                continue;
            }

            var candidate = BalanceMutator.Clone(current);
            foreach (var c in valid)
            {
                BalanceMutator.TryGet(current, c.Path, out var before);
                BalanceMutator.TrySet(candidate, c.Path, c.NewValue, out _);
                Console.WriteLine($"    ✓ {c.Path}: {before:G6} → {c.NewValue:G6}  ({c.Reasoning})");
            }

            BalanceMutator.Install(candidate);
            var newResults = BalanceEvaluator.EvaluateAll(targets, o.Runs, 0, true, o.Seed);
            double newScore = BalanceEvaluator.Score(newResults);
            bool accepted = newScore < score - AcceptEpsilon;

            if (accepted)
            {
                current = candidate;
                results = newResults;
            }
            else
            {
                BalanceMutator.Install(current);
            }

            string verdict = accepted ? "채택" : "되돌림(개선 없음)";
            Console.WriteLine($"  재시뮬레이션 점수 {Fmt(score)} → {Fmt(newScore)} : {verdict}");

            history.Add(new TuningAttempt(it, valid, accepted, score, newScore, accepted ? "점수 개선" : "점수 개선 없음"));
            logs.Add(new IterationLog(it, accepted ? "ACCEPTED" : "REVERTED", valid, rejected, score, newScore, verdict));
            if (accepted) score = newScore;
        }

        BalanceMutator.Install(current);

        var validation = Validate(targets, original, current, o);
        PrintSummary(original, current, initialScore, score, validation);
        WriteOutputs(o, proposer.Name, targetsPath, original, current, initialScore, score, logs, validation);
        return 0;
    }

    // ───────────────────────── 검증 (다른 시드) ─────────────────────────

    private sealed record ValidationResult(int Seed, double OriginalScore, double TunedScore, int OriginalDeviating, int TunedDeviating);

    private static ValidationResult Validate(JsonElement targets, BalanceData original, BalanceData tuned, Options o)
    {
        int seed = o.Seed + 1;
        Console.WriteLine($"\n── 일반화 검증 — 다른 시드({seed})로 원본/튜닝본 재측정 ──");

        BalanceMutator.Install(original);
        var origResults = BalanceEvaluator.EvaluateAll(targets, o.Runs, 0, true, seed);
        BalanceMutator.Install(tuned);
        var tunedResults = BalanceEvaluator.EvaluateAll(targets, o.Runs, 0, true, seed);

        return new ValidationResult(
            seed,
            BalanceEvaluator.Score(origResults), BalanceEvaluator.Score(tunedResults),
            origResults.Count(BalanceEvaluator.IsScored), tunedResults.Count(BalanceEvaluator.IsScored));
    }

    // ───────────────────────── 출력 ─────────────────────────

    private static void PrintSummary(BalanceData original, BalanceData tuned, double initialScore, double finalScore, ValidationResult v)
    {
        Console.WriteLine("\n=== 튜닝 결과 ===");
        Console.WriteLine($"튜닝 시드 점수: {Fmt(initialScore)} → {Fmt(finalScore)}");
        Console.WriteLine($"검증 시드({v.Seed}) 점수: {Fmt(v.OriginalScore)} → {Fmt(v.TunedScore)} (벗어난 지표 {v.OriginalDeviating}개 → {v.TunedDeviating}개)");
        Console.WriteLine(v.TunedScore < v.OriginalScore
            ? "→ 다른 시드에서도 개선이 유지됩니다."
            : "→ 다른 시드에서는 개선이 확인되지 않았습니다(튜닝 시드에 과적합 가능성).");

        var diffs = Diff(original, tuned);
        Console.WriteLine(diffs.Count == 0 ? "\n변경된 파라미터 없음" : "\n변경된 파라미터:");
        foreach (var (path, before, after) in diffs)
            Console.WriteLine($"  {path}: {before:G6} → {after:G6}");
    }

    private static List<(string Path, double Before, double After)> Diff(BalanceData original, BalanceData tuned)
    {
        var diffs = new List<(string, double, double)>();
        foreach (var path in Paths(original))
        {
            if (BalanceMutator.TryGet(original, path, out var a) && BalanceMutator.TryGet(tuned, path, out var b) && Math.Abs(a - b) > 1e-9)
                diffs.Add((path, a, b));
        }
        return diffs;
    }

    private static IEnumerable<string> Paths(BalanceData data)
    {
        static string Camel(string s) => char.ToLowerInvariant(s[0]) + s[1..];

        foreach (var sectionProp in typeof(BalanceData).GetProperties())
        {
            if (sectionProp.GetValue(data) is not { } section) continue;
            foreach (var p in section.GetType().GetProperties())
                if (p.PropertyType == typeof(double) || p.PropertyType == typeof(int))
                    yield return $"{Camel(sectionProp.Name)}.{Camel(p.Name)}";
        }
        foreach (var key in data.Weapons.Table.Keys)
            yield return $"weaponBaseDamage.{key}";
    }

    private static void WriteOutputs(
        Options o, string proposerName, string targetsPath, BalanceData original, BalanceData tuned,
        double initialScore, double finalScore, List<IterationLog> logs, ValidationResult validation)
    {
        string dir = o.OutDir ?? Path.Combine(AppContext.BaseDirectory, "tuning");
        Directory.CreateDirectory(dir);

        string balancePath = Path.Combine(dir, "tuned_balance.json");
        File.WriteAllText(balancePath, BalanceMutator.ToJson(tuned));

        var log = new
        {
            generatedAtUtc = DateTime.UtcNow,
            proposer = proposerName,
            targetsFile = targetsPath,
            runsPerEvaluation = o.Runs,
            seed = o.Seed,
            initialScore,
            finalScore,
            validation = new
            {
                validation.Seed, validation.OriginalScore, validation.TunedScore,
                validation.OriginalDeviating, validation.TunedDeviating,
            },
            changedParameters = Diff(original, tuned).Select(d => new { path = d.Path, before = d.Before, after = d.After }),
            iterations = logs,
        };

        string logPath = Path.Combine(dir, "tuning_log.json");
        File.WriteAllText(logPath, JsonSerializer.Serialize(log, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }));

        Console.WriteLine($"\n튜닝된 밸런스: {balancePath}  (시뮬레이터에 `--balance <경로>`로 그대로 적용 가능)");
        Console.WriteLine($"튜닝 로그: {logPath}");
    }

    // ───────────────────────── 프롬프트 재료 ─────────────────────────

    private static DeviationInfo ToInfo(MetricResult r) => new(r.Group, r.Metric, r.Status, r.Value, r.Min, r.Max, r.Detail);

    private static string BuildGameNotes(BalanceData d)
    {
        var inv = CultureInfo.InvariantCulture;
        var w = d.Wave;
        return string.Join("\n",
            "- 총 30스테이지. 5·15·25스테=중간보스(midBoss*), 10·20·30스테=대형보스(bigBoss*), 나머지=일반 웨이브(mob*). 보스 스테이지는 보스 단독 웨이브.",
            $"- 몬스터 스케일 = {w.ScaleBase.ToString("G4", inv)} + {w.ScalePerStage.ToString("G4", inv)}×(스테-1). {w.DifficultyGateStage1}스테 이상이면 ×{w.DifficultyGateMultiplier.ToString("G4", inv)}, {w.DifficultyGateStage2}스테 이상이면 한 번 더 ×{w.DifficultyGateMultiplier.ToString("G4", inv)}(중첩). 보스 스탯은 최종적으로 ×{w.BossNerfMultiplier.ToString("G4", inv)}.",
            "- 따라서 difficultyGateMultiplier·scalePerStage·bossNerfMultiplier는 11스테 이후 또는 보스 전체에 걸쳐 영향을 주는 '굵은' 파라미터이고, midBoss*/bigBoss*/mob*는 해당 종류 스테이지에만 영향을 준다.",
            "- stageWinRate.N은 N스테에 '도달했을 때' 그 스테이지를 클리어할 조건부 승률이다. runClearRate는 30스테 전체를 한 번에 완주할 확률로, 각 스테이지 승률의 곱에 가깝다.",
            "- stageRewardChoice.*PerStage는 능력치 상자(공격력%/체력)가 스테이지가 오를수록 커지는 정도로, 올리면 후반 난이도가 쉬워진다(적 스탯과 반대 방향).",
            "- wave.stage5/10/20/30BossMultiplier는 해당 스테이지 보스 체력·공격력에만 곱해지는 배율이다. midBoss*는 5·15·25스테, bigBoss*는 10·20·30스테가 공유하므로, 한 스테이지 보스만 쉽거나 어려울 때는 이 스테이지별 배율로 따로 맞추고 공유 파라미터는 나머지 스테이지 기준으로 둔다. 특히 숙련자의 30스테이지 승률은 stage30BossMultiplier로, 20스테이지 벽은 stage20BossMultiplier로 조정한다.",
            "- 설계 의도: 이 게임의 핵심은 '무기 키우기'(자판기 뽑기·합성)다. 장비 상자의 무기는 보조 수단이고(레벨 범위는 표로 고정, 낮춘 레벨 확률은 regularStageLowestLevelChance), 능력치 상자의 공격력%(…AttackPercent*)는 약하게 유지한다 — 공격력%를 올려서 난이도를 맞추지 말고, Lv.5 이상 무기의 공격력(weapons.skillBonusAtLevel5, 올리면 Lv.5~ 무기가 강해짐)이나 적 수치로 맞춘다. 능력치 상자를 안 골라도 클리어할 수 있어야 한다. 이를 재는 지표가 'ExpertWeapon'(능력치 상자 없이 장비 상자만 고르는 숙련자) 그룹의 tier.expertWeapon.reach29다 — 숙련자(Expert, 능력치 상자 위주)와 둘 다 목표 안에 들어와야 한다. 능력치 상자 수치(공격력%·체력)를 낮추거나 장비 상자의 골드(…GoldOption*)를 올려 두 쪽의 격차를 줄인다.",
            "- weaponBaseDamage.X는 해당 무기 종류의 기본 공격력이다. 올리면 그 무기를 쓰는 파티가 강해져 전반적으로 쉬워진다.",
            "- 스테이지/무기 지표는 '균형형 봇'(평균적인 플레이어의 대리)으로 측정한 값이다.",
            "- '숙련자 (Expert)' 그룹은 최적 루트를 아는 숙련자 봇의 지표다: tier.expert.reach29는 29스테이지까지 클리어하는 비율, tier.expert.stage30WinRate는 마지막 30스테이지(최종 보스)의 조건부 승률이다. 평균 플레이어(균형형 봇)의 스테이지 승률 목표와 동시에 맞춰야 하므로, 숙련자만 쉽게 하거나 어렵게 하는 방향으로 한쪽에 치우치지 않는다. 특히 30스테이지 최종 보스 파라미터(bigBoss*)는 10·20스테이지에도 똑같이 적용되니 주의한다.",
            "- '플레이 성향별' 그룹은 저축형/즉시 강화형/보스 대비형 봇을 같은 수치로 돌린 결과다. styleClearRate는 그 성향의 30스테 완주율(너무 높으면 한 성향이 압도적), styleStageRatio는 가장 약한 성향 평균 클리어 스테이지 ÷ 가장 강한 성향 값(낮으면 특정 플레이 방식이 막혀 있다는 뜻)이다. 이 지표들은 적 스탯보다 봇의 행동 차이에서 주로 나오므로, 억지로 맞추려고 스테이지 지표를 크게 해치지 않는다.");
    }

    private static string Fmt(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
}
