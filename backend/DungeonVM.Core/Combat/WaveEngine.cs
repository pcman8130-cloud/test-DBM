using DungeonVM.Core.Balance;
using DungeonVM.Core.Enums;
using DungeonVM.Core.Models;

namespace DungeonVM.Core.Combat;

/// <summary>스테이지 번호에 따라 몬스터 웨이브를 생성한다. 5스테이지 단위 중간 보스, 10스테이지 단위 대형 보스.</summary>
public static class WaveEngine
{
    private static readonly ElementType[] BigBossElementCycle =
    {
        ElementType.Fire, ElementType.Ice, ElementType.Lightning, ElementType.Holy, ElementType.Dark,
    };

    private static WaveBalanceSection Config => BalanceProvider.Current.Wave;

    public static bool IsMidBossStage(int stage) => stage % 5 == 0 && stage % 10 != 0;
    public static bool IsBigBossStage(int stage) => stage % 10 == 0;

    public static ElementType BigBossElementFor(int stage)
        => BigBossElementCycle[(stage / 10 - 1) % BigBossElementCycle.Length];

    public static List<Monster> GenerateWave(int stage, Random rng)
    {
        var config = Config;
        var wave = new List<Monster>();
        double scale = config.ScaleBase + (stage - 1) * config.ScalePerStage;
        if (config.DifficultyGateStage1 > 0 && stage >= config.DifficultyGateStage1)
            scale *= config.DifficultyGateMultiplier;
        if (config.DifficultyGateStage2 > 0 && stage >= config.DifficultyGateStage2)
            scale *= config.DifficultyGateMultiplier;

        int mobCount = config.MobCountBase + stage / config.MobCountStageDivisor;
        for (int i = 0; i < mobCount; i++)
        {
            wave.Add(new Monster(
                name: $"Mob_S{stage}_{i}",
                element: ElementType.None,
                maxHealth: config.MobBaseHealth * scale,
                damage: config.MobBaseDamage * scale,
                attacksPerSecond: config.MobApsMin + rng.NextDouble() * config.MobApsRandomRange,
                goldReward: config.MobGoldBase + stage / config.MobGoldStageDivisor));
        }

        if (IsMidBossStage(stage))
        {
            wave.Add(new Monster(
                name: $"MidBoss_S{stage}",
                element: ElementType.None,
                maxHealth: config.MidBossHealth * scale * config.BossNerfMultiplier,
                damage: config.MidBossDamage * scale * config.BossNerfMultiplier,
                attacksPerSecond: config.MidBossAps,
                goldReward: config.MidBossGoldBase + stage * config.MidBossGoldPerStage,
                isMidBoss: true));
        }

        if (IsBigBossStage(stage))
        {
            var element = BigBossElementFor(stage);
            wave.Add(new Monster(
                name: $"BigBoss_S{stage}_{element}",
                element: element,
                maxHealth: config.BigBossHealth * scale * config.BossNerfMultiplier,
                damage: config.BigBossDamage * scale * config.BossNerfMultiplier,
                attacksPerSecond: config.BigBossAps,
                goldReward: config.BigBossGoldBase + stage * config.BigBossGoldPerStage,
                isBigBoss: true));
        }

        return wave;
    }
}
