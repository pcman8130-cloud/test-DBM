using DungeonVM.Core.Combat;
using DungeonVM.Core.Enums;
using DungeonVM.Core.Systems;
using DungeonVM.Simulator.Bots;
using DungeonVM.Simulator.Metrics;

namespace DungeonVM.Simulator.Evaluation;

/// <summary>
/// 진단용: 봇들이 "어떻게 진행했는지"를 스테이지 시작 시점의 파티 상태로 비교한다(인원·무기 티어·방어구·체력·공격력).
/// 결과(평균 클리어 스테이지)만 보면 봇이 왜 약한지 알 수 없어서, 보상 선택 말고 다른 성장(인원·무기·자판기·그리드)을 같이 했는지 본다.
/// 사용: --compare-bots Balanced,StatOnly,... [--seed N] [런 수]
/// </summary>
public static class BotComparison
{
    private static readonly int[] Checkpoints = { 3, 5, 8, 10, 15 };

    public static int Run(string[] names, int runs, int seed)
    {
        Console.WriteLine($"봇 진행 비교 — {runs}회, 시드 {seed}. 각 스테이지 '시작(정비 직후)' 시점의 평균 (그 스테이지에 도달한 런만).");
        foreach (var name in names)
        {
            var bot = BotCatalog.Create(name);
            var rng = new Random(seed);
            var weapons = new WeaponTierMetrics();
            var attempts = new StageAttemptCollector();
            var snap = new Dictionary<int, List<double[]>>();
            foreach (var s in Checkpoints) snap[s] = new List<double[]>();
            var ends = new List<RunResult>();

            void OnStart(int stage, StageLoop loop)
            {
                if (!snap.TryGetValue(stage, out var list)) return;
                var p = loop.Party;
                list.Add(new[]
                {
                    p.Count,
                    p.Average(c => c.EquippedWeapon?.Tier ?? 0),
                    p.Max(c => c.EquippedWeapon?.Tier ?? 0),
                    p.Count(c => c.EquippedArmor is not null),
                    p.Average(c => c.MaxHealth),
                    p.Average(c => c.AttackDamage),
                    loop.Machine.AttackUpgradeLevel,
                });
            }

            for (int i = 0; i < runs; i++)
            {
                var r = Program.SimulateOneRun(bot, i, rng, weapons, attempts, onStageStart: OnStart);
                ends.Add(r);
            }

            Console.WriteLine($"\n[{name}] 평균 클리어 {ends.Average(e => e.StagesCleared):F1}스테, 완주 {ends.Count(e => e.Outcome == RunEndReason.Victory) / (double)runs:P1}");
            Console.WriteLine("  스테  도달   인원  평균무기Lv 최고Lv 방어구수  평균HP  평균공격력  자판기공격Lv");
            foreach (var s in Checkpoints)
            {
                var l = snap[s];
                if (l.Count == 0) { Console.WriteLine($"  {s,3}  {0,5:P0}"); continue; }
                double A(int k) => l.Average(x => x[k]);
                Console.WriteLine($"  {s,3}  {l.Count / (double)runs,5:P0}  {A(0),4:F1}  {A(1),8:F1}  {A(2),6:F1}  {A(3),6:F1}  {A(4),7:F0}  {A(5),9:F1}  {A(6),10:F1}");
            }
            var stages = attempts.ByStage(bot.Name).ToDictionary(x => x.Stage);
            Console.WriteLine("  보스 스테이지 조건부 승률(도달 시) / 누적 도달률(그 스테이지를 클리어한 비율):");
            foreach (var s in new[] { 5, 10, 15, 20, 25, 30 })
            {
                double win = stages.TryGetValue(s, out var st) && st.Attempts > 0 ? st.WinRate : double.NaN;
                int reach = ends.Count(e => e.StagesCleared >= s);
                Console.WriteLine($"    {s,2}스테  승률 {win,6:P1}   클리어 {reach / (double)runs,6:P1}");
            }
            Console.WriteLine($"  종료 시점: 인원 {ends.Average(e => e.PartySize):F1}, 그리드 {ends.Average(e => e.GridUnlockedCells):F1}칸, 자판기 공격 {ends.Average(e => e.AttackUpgradeLevel):F1} / 방어 {ends.Average(e => e.DefenseUpgradeLevel):F1}");
        }
        return 0;
    }
}
