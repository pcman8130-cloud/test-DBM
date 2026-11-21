using DungeonVM.Core.Combat;
using DungeonVM.Core.Enums;
using DungeonVM.Core.Models;
using DungeonVM.Core.Systems;
using DungeonVM.Simulator.Bots;
using DungeonVM.Simulator.Metrics;

namespace DungeonVM.Simulator.Evaluation;

/// <summary>
/// 진단용: 30스테이지를 클리어한 런의 "최종 스펙"(파티 구성, 무기 레벨, 방어구, 룬, 자판기 레벨, 능력치, 유물)을 모아서 보여 준다.
/// 비교를 위해 30스테이지까지 갔지만 졌던 런(29스테이지까지 클리어)도 같이 모아 두 그룹의 차이를 본다.
/// 사용: --clear-profile Population,Expert [런 수] [--seed N] --skip-llm
/// </summary>
public static class ClearProfile
{
    private sealed record Snap(
        int Party, List<int> TiersDesc, List<WeaponType> Types, List<ElementType> Elements, List<ArmorRarity> Armors,
        int AtkLevel, int DefLevel, int GridCells, double AvgHp, double AvgAtk, double AvgAps, double AvgDodge,
        List<string> Relics, int Stages);

    public static int Run(string[] names, int runs, int seed)
    {
        foreach (var name in names)
        {
            var bot = BotCatalog.Create(name);
            var rng = new Random(seed);
            var weapons = new WeaponTierMetrics();
            var attempts = new StageAttemptCollector();
            var won = new List<Snap>();
            var lost30 = new List<Snap>();
            int total = 0;

            void OnEnd(RunResult r, StageLoop loop)
            {
                total++;
                bool victory = r.Outcome == RunEndReason.Victory;
                if (!victory && r.StagesCleared < StageLoop.MaxStage - 1) return;
                var p = loop.Party;
                var snap = new Snap(
                    p.Count,
                    p.Select(c => c.EquippedWeapon?.Tier ?? 0).OrderByDescending(x => x).ToList(),
                    p.Where(c => c.EquippedWeapon is not null).Select(c => c.EquippedWeapon!.Type).ToList(),
                    p.Where(c => c.EquippedWeapon is not null).Select(c => c.EquippedWeapon!.Element).ToList(),
                    p.Where(c => c.EquippedArmor is not null).Select(c => c.EquippedArmor!.Rarity).ToList(),
                    loop.Machine.AttackUpgradeLevel, loop.Machine.DefenseUpgradeLevel, loop.Inventory.Grid.UnlockedCells,
                    p.Average(c => c.MaxHealth), p.Average(c => c.AttackDamage), p.Average(c => c.AttacksPerSecond), p.Average(c => c.DodgeChance),
                    loop.OwnedRelicIds.Where(id => RelicCatalog.Get(id) is not { Kind: RelicKind.Boss, UnlockStage: 30 }).ToList(), r.StagesCleared); // 30스테 보스 유물은 클리어 직후 받는 보상이라 제외
                (victory ? won : lost30).Add(snap);
            }

            for (int i = 0; i < runs; i++)
                Program.SimulateOneRun(bot, i, rng, weapons, attempts, onRunEnd: OnEnd);

            Console.WriteLine($"\n==================== {name} — {total}회 중 30스테 클리어 {won.Count}회 ({won.Count / (double)total:P1}), 30스테 도전했다가 패배 {lost30.Count}회 ====================");
            if (won.Count == 0) { Console.WriteLine("클리어한 런이 없어 건너뜁니다."); continue; }

            Console.WriteLine("\n[요약 비교]  클리어 / 30스테 패배");
            void Row(string label, Func<Snap, double> f, string fmt = "F1")
                => Console.WriteLine($"  {label,-22} {won.Average(f).ToString(fmt),8} / {(lost30.Count == 0 ? "-" : lost30.Average(f).ToString(fmt)),8}");
            Row("파티 인원", s => s.Party);
            Row("무기 평균 레벨", s => s.TiersDesc.Average());
            Row("무기 최고 레벨", s => s.TiersDesc.Max());
            Row("무기 최저 레벨", s => s.TiersDesc.Min());
            Row("자판기 공격 Lv", s => s.AtkLevel);
            Row("자판기 방어 Lv", s => s.DefLevel);
            Row("그리드 칸 수", s => s.GridCells);
            Row("평균 체력(유물 포함)", s => s.AvgHp, "F0");
            Row("평균 공격력(유물 제외)", s => s.AvgAtk);
            Row("평균 공격속도", s => s.AvgAps, "F2");
            Row("평균 회피율", s => s.AvgDodge * 100, "F1");
            Row("룬 장착 무기 수", s => s.Elements.Count(e => e != ElementType.None));
            Row("보유 유물 수", s => s.Relics.Count);

            Console.WriteLine("\n[클리어 파티: 무기 레벨 분포 — 높은 순 슬롯별 평균]");
            int maxParty = won.Max(s => s.Party);
            for (int slot = 0; slot < maxParty; slot++)
            {
                var have = won.Where(s => s.TiersDesc.Count > slot).Select(s => s.TiersDesc[slot]).ToList();
                Console.WriteLine($"  {slot + 1}번째로 높은 무기: 평균 Lv.{have.Average():F1}  (최소 {have.Min()}, 최대 {have.Max()}, {have.Count}런)");
            }
            Console.WriteLine("  최고 무기 레벨 분포: " + string.Join("  ", won.GroupBy(s => s.TiersDesc.Max()).OrderBy(g => g.Key).Select(g => $"Lv.{g.Key} {g.Count() / (double)won.Count:P0}")));
            Console.WriteLine("  파티 인원 분포: " + string.Join("  ", won.GroupBy(s => s.Party).OrderBy(g => g.Key).Select(g => $"{g.Key}명 {g.Count() / (double)won.Count:P0}")));

            Console.WriteLine("\n[클리어 파티: 무기 종류 채택률(무기 1개당)]");
            var allTypes = won.SelectMany(s => s.Types).ToList();
            Console.WriteLine("  " + string.Join("  ", allTypes.GroupBy(t => t).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count() / (double)allTypes.Count:P0}")));
            Console.WriteLine("[클리어 파티: 룬 속성(무기 1개당)]");
            var allEl = won.SelectMany(s => s.Elements).ToList();
            Console.WriteLine("  " + string.Join("  ", allEl.GroupBy(t => t).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count() / (double)allEl.Count:P0}")));
            Console.WriteLine("[클리어 파티: 방어구 등급(방어구 1개당)]");
            var allAr = won.SelectMany(s => s.Armors).ToList();
            Console.WriteLine("  " + string.Join("  ", allAr.GroupBy(t => t).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count() / (double)allAr.Count:P0}")));

            Console.WriteLine("\n[유물 보유율]  클리어 런 / 30스테 도달 런 중 클리어 비율(그 유물이 있는 런 기준)");
            var reach = won.Concat(lost30).ToList();
            var ids = reach.SelectMany(s => s.Relics).Distinct().ToList();
            var rows = ids.Select(id =>
            {
                var def = RelicCatalog.Get(id);
                int w = won.Count(s => s.Relics.Contains(id));
                int with = reach.Count(s => s.Relics.Contains(id));
                return (Id: id, Name: def?.Name ?? id, Kind: def?.Kind.ToString() ?? "", Stage: def?.UnlockStage ?? 0, WonRate: w / (double)won.Count, Win: with == 0 ? 0 : w / (double)with, With: with);
            }).ToList();

            Console.WriteLine("  -- 보스 유물(스테이지별 선택) --");
            foreach (var g in rows.Where(r => r.Kind == "Boss").GroupBy(r => r.Stage).OrderBy(g => g.Key))
            {
                Console.WriteLine($"  {g.Key,2}스테: " + string.Join("  |  ", g.OrderByDescending(r => r.WonRate).Select(r => $"{r.Name} {r.WonRate:P0} (승률 {r.Win:P0}/{r.With}런)")));
            }
            Console.WriteLine("  -- 일반 유물(보유율 높은 순 상위 12) --");
            foreach (var r in rows.Where(r => r.Kind != "Boss").OrderByDescending(r => r.WonRate).Take(12))
                Console.WriteLine($"   {r.Name,-14} 보유 {r.WonRate:P0}   (보유 런 {r.With}개 중 클리어 {r.Win:P0})");
        }
        return 0;
    }
}
