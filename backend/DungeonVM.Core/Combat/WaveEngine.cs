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

        // 중간보스/보스 스테이지는 프로토타입(makeWave)과 동일하게 그 보스 "단독" 웨이브다 — 일반 몹과
        // 같이 나오지 않는다. 예전엔 일반 몹 mobCount마리를 무조건 먼저 채운 뒤 보스를 추가로 얹었는데,
        // 그러면 동시교전수(3) 전부가 일반 몹 focus-fire에 낭비되고 보스는 뒷전으로 밀리는 데다,
        // 전투가 훨씬 길어져 파티가 순차적으로 갈려나가는 원인이 되었다(스테15 승률 0% 원인으로 확인됨).
        // 보스 공통 하향 배율 × (5·10·20·30스테 보스만 따로 곱하는 스테이지별 배율)
        double bossMult = config.BossNerfMultiplier * stage switch
        {
            5 => config.Stage5BossMultiplier,
            10 => config.Stage10BossMultiplier,
            20 => config.Stage20BossMultiplier,
            30 => config.Stage30BossMultiplier,
            _ => 1.0,
        };

        if (IsMidBossStage(stage))
        {
            wave.Add(new Monster(
                name: $"MidBoss_S{stage}",
                element: ElementType.None,
                maxHealth: config.MidBossHealth * scale * bossMult,
                damage: config.MidBossDamage * scale * bossMult,
                attacksPerSecond: config.MidBossAps,
                goldReward: config.MidBossGoldBase + stage * config.MidBossGoldPerStage,
                isMidBoss: true));
            return wave;
        }

        if (IsBigBossStage(stage))
        {
            var element = BigBossElementFor(stage);
            wave.Add(new Monster(
                name: $"BigBoss_S{stage}_{element}",
                element: element,
                maxHealth: config.BigBossHealth * scale * bossMult,
                damage: config.BigBossDamage * scale * bossMult,
                attacksPerSecond: config.BigBossAps,
                goldReward: config.BigBossGoldBase + stage * config.BigBossGoldPerStage,
                isBigBoss: true));
            return wave;
        }

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

        return wave;
    }
}
