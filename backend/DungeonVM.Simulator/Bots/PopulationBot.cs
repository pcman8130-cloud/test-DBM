using DungeonVM.Core.Enums;
using DungeonVM.Core.Models;
using DungeonVM.Core.Systems;

namespace DungeonVM.Simulator.Bots;

/// <summary>PopulationBot이 런마다 뽑는 플레이어 유형.</summary>
public enum Archetype { Novice, Casual, Skilled }

/// <summary>
/// "플레이어 집단" 봇: 런이 시작될 때마다 플레이어 한 명을 확률적으로 뽑아서 그 사람처럼 한 판을 한다.
/// 고정된 봇 몇 개(균형형/숙련자 등)는 제가 추측한 성향이라 실제 플레이어 전체를 대표하지 못한다 — 이 봇은 유형(초보/보통/숙련)을 정해진
/// 비율로 섞고, 유형 안에서도 저축 성향·상자 고르는 습관·정리/룬 장착 여부·실수 확률을 무작위로 뽑아 넓은 분포를 만든다.
/// 이 분포(유형 비율, 각 습관의 확률)는 코드 상수로 두었고, 실제 플레이 데이터(텔레메트리)가 모이면 그에 맞춰 보정한다.
/// 상자 고르기의 "실수"는 확률 eps로 그 상자를 무작위로 대체한다(능력치만/장비만 같은 일관된 습관과는 별개).
/// </summary>
public sealed class PopulationBot : IBot
{
    /// <summary>유형 비율(초보, 보통, 숙련). 합이 1이 아니어도 비율로 쓴다. 평가기가 BalanceTargets.json의 population.archetypeWeights로 덮어쓴다.</summary>
    public static double[] ArchetypeWeights { get; private set; } = { 0.30, 0.45, 0.25 };

    public static void SetWeights(double novice, double casual, double skilled)
        => ArchetypeWeights = new[] { Math.Max(novice, 0), Math.Max(casual, 0), Math.Max(skilled, 0) };

    private BotContext? _ctx;
    private BalancedBot _inner = new();

    /// <summary>현재(마지막) 런의 유형. 평가기가 런이 끝난 뒤 읽어서 유형별로 집계한다.</summary>
    public Archetype LastArchetype { get; private set; } = Archetype.Casual;

    /// <summary>현재(마지막) 런에서 뽑힌 성향 설명(디버깅용).</summary>
    public string LastProfileName { get; private set; } = "";

    public string Name => "Population";

    // ───────────────────────── 런마다 플레이어 뽑기 ─────────────────────────

    private BalancedBot Sync(BotContext ctx)
    {
        if (!ReferenceEquals(ctx, _ctx))
        {
            _ctx = ctx;
            _inner = new BalancedBot(Draw(ctx.Rng));
        }
        return _inner;
    }

    private static T Pick<T>(Random rng, params (T Item, double Weight)[] items)
    {
        double total = items.Sum(i => i.Weight), r = rng.NextDouble() * total;
        foreach (var (item, w) in items) { if ((r -= w) < 0) return item; }
        return items[^1].Item;
    }

    private static RewardPolicy Mistaky(RewardPolicy policy, double eps)
        => (ctx, choice) => ctx.Rng.NextDouble() < eps ? ctx.Rng.Next(choice.Options.Count) : policy(ctx, choice);

    private BalancedBotProfile Draw(Random rng)
    {
        var w = ArchetypeWeights;
        var type = Pick(rng, (Archetype.Novice, w[0]), (Archetype.Casual, w[1]), (Archetype.Skilled, w[2]));
        LastArchetype = type;

        // 저축 성향은 실력이 아니라 "스타일"이라 유형과 무관하게 같은 분포에서 뽑는다(0=번 돈을 바로 씀 … 6=오래 모아서 한꺼번에).
        // 시뮬레이션에서도 번 돈을 바로 쓰는 쪽이 저축형보다 멀리 간다(Spender 20.5스테 vs Hoarder 13.1스테 — 플레이 경험과도 일치) —
        // 그래서 바로 쓰는 사람이 더 많은 분포로 둔다.
        int saving = Pick(rng, (0, 25), (1, 40), (3, 25), (6, 10));

        BalancedBotProfile profile;
        switch (type)
        {
            case Archetype.Novice:
            {
                var reward = Pick<RewardPolicy>(rng,
                    (RewardPolicies.RandomBox, 40), (RewardPolicies.Stat, 20), (RewardPolicies.AlwaysEquipment, 20), (RewardPolicies.PartyFirstThenStat, 20));
                profile = new BalancedBotProfile("Novice", saving, Mistaky(reward, 0.25),
                    MakeRoomWhenFull: rng.NextDouble() < 0.2, SocketRunes: rng.NextDouble() < 0.3,
                    RandomCandidates: rng.NextDouble() < 0.6, RandomRelics: rng.NextDouble() < 0.7);
                break;
            }
            case Archetype.Casual:
            {
                var reward = Pick<RewardPolicy>(rng,
                    (RewardPolicies.RuneHungry, 30), (RewardPolicies.PartyFirstThenStat, 20), (RewardPolicies.AlwaysEquipment, 15),
                    (RewardPolicies.Stat, 10), (RewardPolicies.SpecialBeforeBoss(RewardPolicies.Stat), 15), (RewardPolicies.Special, 5), (RewardPolicies.RandomBox, 5));
                profile = new BalancedBotProfile("Casual", saving, Mistaky(reward, 0.08),
                    MakeRoomWhenFull: rng.NextDouble() < 0.7, SocketRunes: rng.NextDouble() < 0.8,
                    RandomCandidates: rng.NextDouble() < 0.1, RandomRelics: rng.NextDouble() < 0.2);
                break;
            }
            default:
            {
                var baseProfile = Pick(rng,
                    (BalancedBotProfile.Expert, 30), (BalancedBotProfile.ExpertWeapon, 15), (BalancedBotProfile.BossPrep, 20),
                    (BalancedBotProfile.Spender, 20), (BalancedBotProfile.Default, 15));
                profile = baseProfile with { Reward = Mistaky(baseProfile.Reward, 0.03) };
                break;
            }
        }

        LastProfileName = $"{type}/{profile.Name}/save{profile.SaveStagesAhead}";
        return profile;
    }

    // ───────────────────────── IBot (현재 런의 내부 봇에 위임) ─────────────────────────

    public void OnMaintenancePhase(BotContext ctx) => Sync(ctx).OnMaintenancePhase(ctx);
    public void OnCombatTick(BotContext ctx) => Sync(ctx).OnCombatTick(ctx);
    public int ChooseStageReward(BotContext ctx, StageRewardChoice choice) => Sync(ctx).ChooseStageReward(ctx, choice);
    public int ChooseRewardCandidate(BotContext ctx, StageRewardReveal reveal) => Sync(ctx).ChooseRewardCandidate(ctx, reveal);
    public WeaponType ChooseBannedWeapon(BotContext ctx) => Sync(ctx).ChooseBannedWeapon(ctx);
    public int ChooseBossRelic(BotContext ctx, IReadOnlyList<BossRelic> candidates) => Sync(ctx).ChooseBossRelic(ctx, candidates);
}
