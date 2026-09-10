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

    public static bool IsMidBossStage(int stage) => stage % 5 == 0 && stage % 10 != 0;
    public static bool IsBigBossStage(int stage) => stage % 10 == 0;

    public static ElementType BigBossElementFor(int stage)
        => BigBossElementCycle[(stage / 10 - 1) % BigBossElementCycle.Length];

    public static List<Monster> GenerateWave(int stage, Random rng)
    {
        var wave = new List<Monster>();
        double scale = 1 + (stage - 1) * 0.07;

        int mobCount = 3 + stage / 4;
        for (int i = 0; i < mobCount; i++)
        {
            wave.Add(new Monster(
                name: $"Mob_S{stage}_{i}",
                element: ElementType.None,
                maxHealth: 14 * scale,
                damage: 2.2 * scale,
                attacksPerSecond: 0.6 + rng.NextDouble() * 0.3,
                goldReward: 3 + stage / 2));
        }

        if (IsMidBossStage(stage))
        {
            wave.Add(new Monster(
                name: $"MidBoss_S{stage}",
                element: ElementType.None,
                maxHealth: 220 * scale,
                damage: 10 * scale,
                attacksPerSecond: 0.6,
                goldReward: 40 + stage * 2,
                isMidBoss: true));
        }

        if (IsBigBossStage(stage))
        {
            var element = BigBossElementFor(stage);
            wave.Add(new Monster(
                name: $"BigBoss_S{stage}_{element}",
                element: element,
                maxHealth: 600 * scale,
                damage: 18 * scale,
                attacksPerSecond: 0.5,
                goldReward: 120 + stage * 4,
                isBigBoss: true));
        }

        return wave;
    }
}
