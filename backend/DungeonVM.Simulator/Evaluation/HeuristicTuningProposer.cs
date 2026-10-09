using DungeonVM.LLM.Tuning;

namespace DungeonVM.Simulator.Evaluation;

/// <summary>
/// API 키 없이 튜닝 루프(제안 → 화이트리스트 검증 → 재시뮬레이션 → 채택/되돌림)가 끝까지 동작하는지 검증하기 위한
/// 규칙 기반 대역. 실제 LLM이 아니며 "어떤 지표가 어떤 파라미터와 연결되는지"를 하드코딩한 단순 규칙이다
/// (보스 체력/데미지, 일반 몹 체력/데미지, 난이도 게이트만 다룸). `--mock-llm`으로 선택한다.
/// </summary>
internal sealed class HeuristicTuningProposer : ITuningProposer
{
    private const double StepGain = 0.5;
    private const double MaxStep = 0.12;

    public string Name => "Heuristic(규칙 기반 대역 — LLM 아님)";

    public Task<IReadOnlyList<ParameterChange>> ProposeAsync(TuningContext context, CancellationToken ct = default)
    {
        // 파라미터별로 "올려야 하면 +, 내려야 하면 -" 압력을 누적한다. 지표가 너무 쉬우면(HIGH) 적 스탯을 올린다.
        var pressure = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        void Push(string path, double amount) => pressure[path] = pressure.GetValueOrDefault(path) + amount;

        foreach (var d in context.Deviations)
        {
            double sign = d.Status == "HIGH" ? 1 : -1; // 승률/완주율이 높다(쉽다) → 적을 강하게
            double bound = Math.Max(Math.Abs(d.Status == "LOW" ? d.Min ?? 0 : d.Max ?? 0), 0.05);
            double size = d.Value is { } v ? Math.Abs(v - (d.Status == "LOW" ? d.Min ?? v : d.Max ?? v)) / bound : 0;

            if (d.Metric.StartsWith("stageWinRate.", StringComparison.Ordinal) && int.TryParse(d.Metric["stageWinRate.".Length..], out int stage))
            {
                if (stage % 10 == 0)
                {
                    Push("wave.bigBossHealth", sign * size);
                    Push("wave.bigBossDamage", sign * size * 0.7);
                }
                else if (stage % 5 == 0)
                {
                    Push("wave.midBossHealth", sign * size);
                    Push("wave.midBossDamage", sign * size * 0.7);
                }
                else
                {
                    Push("wave.mobBaseHealth", sign * size);
                    Push("wave.mobBaseDamage", sign * size * 0.7);
                }
            }
            else if (d.Metric == "runClearRate")
            {
                Push("wave.difficultyGateMultiplier", sign * size);
            }
        }

        // 이전 시도가 되돌려질 때마다 스텝을 절반으로 줄인다(같은 제안을 반복하지 않고 더 작게 재시도하는 후퇴 규칙).
        int reverted = context.History.Count(h => !h.Accepted && h.Changes.Count > 0);
        double backoff = Math.Pow(0.5, reverted);

        var changes = new List<ParameterChange>();
        foreach (var (path, p) in pressure.OrderByDescending(kv => Math.Abs(kv.Value)).Take(context.MaxChanges))
        {
            var param = context.Tunables.FirstOrDefault(t => t.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (param is null || Math.Abs(p) < 1e-6) continue;

            double step = Math.Clamp(p * StepGain, -MaxStep, MaxStep) * backoff;
            double next = Math.Clamp(param.Current * (1 + step), param.Min, param.Max);
            next = Math.Round(next, 3);
            string dir = step > 0 ? "상향" : "하향";
            changes.Add(new ParameterChange(param.Path, next, $"연결된 지표 편차의 순압력 {p:+0.00;-0.00} → {dir} {Math.Abs(step):P0}"));
        }

        return Task.FromResult<IReadOnlyList<ParameterChange>>(changes);
    }
}
