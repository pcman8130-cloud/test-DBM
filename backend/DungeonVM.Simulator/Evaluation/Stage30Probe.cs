using DungeonVM.Core.Balance;
using DungeonVM.Core.Combat;
using DungeonVM.Core.Enums;
using DungeonVM.Core.Models;
using DungeonVM.Core.Systems;
using DungeonVM.Simulator.Bots;
using DungeonVM.Simulator.Metrics;

namespace DungeonVM.Simulator.Evaluation;

/// <summary>
/// 진단용: 봇이 30스테이지까지 만든 최종 파티(30스테를 깼든 졌든)를 그대로 가져와서, 한 가지씩만 바꿔 30스테 보스를 다시 싸워 본다.
/// "방어구를 전부 레전더리로 바꾸면?", "무기를 한 레벨씩 올리면?" 같은 질문에 같은 빌드끼리 비교해서 답하려는 용도다
/// (봇마다 빌드가 달라 서로 비교하면 원인을 알 수 없어서). 재전투 중에는 봇이 아무 행동도 하지 않는다(합성·뽑기 없음).
/// 사용: --stage30-probe Population,Balanced [런 수] --seed N --skip-llm
/// </summary>
public static class Stage30Probe
{
    private const double TickSeconds = 0.25;
    private const double MaxSeconds = 60;
    private const int FightsPerVariant = 3;

    private sealed record Build(List<(WeaponType Type, int Tier, ElementType Element)?> Weapons, List<ArmorRarity?> Armors, double AvgTier);

    private static Armor MeanArmor(ArmorRarity rarity)
    {
        var r = BalanceProvider.Current.Armor.RollRanges[rarity.ToString()];
        return new Armor(ArmorType.Plate, rarity, (r.MinHp + r.MaxHp) / 2, (r.MinAtk + r.MaxAtk) / 2, (r.MinDodge + r.MaxDodge) / 2);
    }

    private static readonly (string Label, Action<List<Character>> Apply)[] Variants =
    {
        ("그대로(봇이 만든 파티)", _ => { }),
        ("방어구 전원 일반", p => { foreach (var c in p) c.EquipArmor(MeanArmor(ArmorRarity.Common)); }),
        ("방어구 전원 레어", p => { foreach (var c in p) c.EquipArmor(MeanArmor(ArmorRarity.Rare)); }),
        ("방어구 전원 에픽", p => { foreach (var c in p) c.EquipArmor(MeanArmor(ArmorRarity.Epic)); }),
        ("방어구 전원 레전더리", p => { foreach (var c in p) c.EquipArmor(MeanArmor(ArmorRarity.Legendary)); }),
        ("무기 전원 +1레벨", p => Upgrade(p, 1)),
        ("무기 전원 +2레벨", p => Upgrade(p, 2)),
        ("무기 전원 Lv.9(최대급)", p => { foreach (var c in p) if (c.EquippedWeapon is { } w) c.EquipWeaponFreely(new Weapon(w.Type, 9, w.Element)); }),
    };

    private static void Upgrade(List<Character> party, int levels)
    {
        foreach (var c in party)
            if (c.EquippedWeapon is { } w) c.EquipWeaponFreely(new Weapon(w.Type, Math.Min(w.Tier + levels, 9), w.Element));
    }

    public static int Run(string[] names, int runs, int seed)
    {
        foreach (var name in names)
        {
            var bot = BotCatalog.Create(name);
            var rng = new Random(seed);
            var weapons = new WeaponTierMetrics();
            var attempts = new StageAttemptCollector();
            var wins = new int[Variants.Length]; var fights = new int[Variants.Length];
            var winsHi = new int[Variants.Length]; var fightsHi = new int[Variants.Length];
            int snaps = 0, snapsHi = 0;

            void OnEnd(RunResult r, StageLoop loop)
            {
                if (r.Outcome != RunEndReason.Victory && r.StagesCleared < StageLoop.MaxStage - 1) return;
                snaps++;
                var party = loop.Party;
                double avgTier = party.Average(c => c.EquippedWeapon?.Tier ?? 0);
                bool hi = avgTier >= 6.5;
                if (hi) snapsHi++;

                // 원본 빌드를 기억해 두고 변형마다 복원한다.
                var weaponsBackup = party.Select(c => c.EquippedWeapon is { } w ? ((WeaponType, int, ElementType)?)(w.Type, w.Tier, w.Element) : null).ToList();
                var armorsBackup = party.Select(c => c.EquippedArmor).ToList();

                for (int v = 0; v < Variants.Length; v++)
                {
                    for (int k = 0; k < FightsPerVariant; k++)
                    {
                        // 복원
                        for (int i = 0; i < party.Count; i++)
                        {
                            if (weaponsBackup[i] is { } b) party[i].EquipWeaponFreely(new Weapon(b.Item1, b.Item2, b.Item3));
                            if (armorsBackup[i] is { } a) party[i].EquipArmor(a);
                        }
                        Variants[v].Apply(party);
                        foreach (var c in party) { c.ReviveNow(); c.Heal(1e9); }

                        var fr = new Random(seed * 31 + snaps * 7 + k);
                        var monsters = WaveEngine.GenerateWave(30, fr);
                        var battle = new BattleField(party, monsters, fr, loop.Currency, loop);
                        var outcome = RunEndReason.InProgress;
                        for (double t = 0; t < MaxSeconds && outcome == RunEndReason.InProgress; t += TickSeconds)
                            outcome = battle.Tick(TickSeconds);
                        bool win = outcome == RunEndReason.Victory;
                        fights[v]++; if (win) wins[v]++;
                        if (hi) { fightsHi[v]++; if (win) winsHi[v]++; }
                    }
                }

                for (int i = 0; i < party.Count; i++)
                {
                    if (weaponsBackup[i] is { } b) party[i].EquipWeaponFreely(new Weapon(b.Item1, b.Item2, b.Item3));
                    if (armorsBackup[i] is { } a) party[i].EquipArmor(a);
                }
            }

            for (int i = 0; i < runs; i++)
                Program.SimulateOneRun(bot, i, rng, weapons, attempts, onRunEnd: OnEnd);

            Console.WriteLine($"\n==================== {name} — 30스테에 도달한 최종 파티 {snaps}개(그중 무기 평균 Lv.6.5↑ {snapsHi}개), 변형마다 {FightsPerVariant}번씩 30스테 보스전 ====================");
            Console.WriteLine($"{"변형",-26} {"전체 승률",9} {"무기 Lv6.5↑ 승률",18}");
            for (int v = 0; v < Variants.Length; v++)
                Console.WriteLine($"{Variants[v].Label,-24} {wins[v] / (double)Math.Max(fights[v], 1),10:P1} {(fightsHi[v] == 0 ? "-" : (winsHi[v] / (double)fightsHi[v]).ToString("P1")),16}");
        }
        return 0;
    }
}
