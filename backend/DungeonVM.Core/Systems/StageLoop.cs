using DungeonVM.Core.Balance;
using DungeonVM.Core.Combat;
using DungeonVM.Core.Enums;
using DungeonVM.Core.Inventory;
using DungeonVM.Core.Models;

namespace DungeonVM.Core.Systems;

/// <summary>1~30 스테이지 진행, 웨이브 생성, 클리어 보상, 정비 페이즈 전환을 조율하는 최상위 오케스트레이터.</summary>
public sealed class StageLoop
{
    private static readonly ElementType[] RuneElements =
    {
        ElementType.Fire, ElementType.Ice, ElementType.Lightning, ElementType.Holy, ElementType.Dark, ElementType.Poison,
    };

    private static StageLoopBalanceSection Config => BalanceProvider.Current.StageLoop;
    private static CharacterSlotBalanceSection SlotConfig => BalanceProvider.Current.CharacterSlots;
    private static StageRewardChoiceBalanceSection RewardConfig => BalanceProvider.Current.StageRewardChoice;

    public static int MaxStage => Config.MaxStage;

    public int CurrentStage { get; private set; } = 1;
    public StagePhase Phase { get; private set; } = StagePhase.Maintenance;

    public CurrencyManager Currency { get; }
    public InventoryManager Inventory { get; }
    public VendingMachine Machine { get; } = new();
    public List<Character> Party { get; }

    public int SoulsEarnedThisRun { get; private set; }
    public bool IsRunComplete => CurrentStage > MaxStage;

    /// <summary>이번 런에서 상자 선택보상으로 획득한 유물 목록(패시브 효과는 획득 즉시 적용됨).</summary>
    public List<Relic> Relics { get; } = new();

    public double RelicRetireSpeedBonus { get; private set; }
    public double RelicGoldGainBonus { get; private set; }
    public double RelicUpgradeDiscountRatio { get; private set; }

    public StageLoop(CurrencyManager currency, InventoryManager inventory, List<Character> party)
    {
        Currency = currency;
        Inventory = inventory;
        Party = party;
    }

    /// <summary>정비 페이즈를 마치고 현재 스테이지의 전투 웨이브를 생성한다.</summary>
    public List<Monster> BeginStageCombat(Random rng)
    {
        Phase = StagePhase.Combat;
        return WaveEngine.GenerateWave(CurrentStage, rng);
    }

    /// <summary>
    /// 스테이지 클리어 처리: 등급(기본/중간보스/보스)에 따른 기본보상을 즉시 지급하고, 전원 자동 부활 후
    /// 다음 스테이지로 진행한다. 이어서 골라야 할 3개 선택보상(골드/팀 능력치 영구증가/상자)을 반환하므로,
    /// 호출자(봇 정책 또는 Unity UI)가 하나를 골라 ResolveStageRewardChoice로 확정해야 한다.
    /// </summary>
    public StageRewardChoice CompleteStageVictory(Random rng)
    {
        var config = Config;
        var rewardConfig = RewardConfig;

        int goldReward = config.VictoryGoldBase + CurrentStage * config.VictoryGoldPerStage;
        Currency.Add(CurrencyType.Gold, (int)(goldReward * (1 + RelicGoldGainBonus)));

        var tier = WaveEngine.IsBigBossStage(CurrentStage) ? StageTier.Boss
            : WaveEngine.IsMidBossStage(CurrentStage) ? StageTier.MidBoss
            : StageTier.Regular;

        int soulsReward = tier switch
        {
            StageTier.Boss => rewardConfig.BossBaseSoulsBonus,
            StageTier.MidBoss => rewardConfig.MidBossBaseSoulsBonus,
            _ => 0, // 기본 스테이지는 영혼을 주지 않는다(영혼은 중간보스/보스 기본보상 전용).
        };
        SoulsEarnedThisRun += soulsReward;

        var options = BuildRewardOptions(tier, rewardConfig);

        foreach (var c in Party)
            c.ReviveNow();

        Phase = StagePhase.Maintenance;
        CurrentStage++;

        return new StageRewardChoice(tier, options);
    }

    private static List<StageRewardOption> BuildRewardOptions(StageTier tier, StageRewardChoiceBalanceSection cfg) => tier switch
    {
        StageTier.Boss => new List<StageRewardOption>
        {
            new(StageRewardOptionType.Gold, GoldAmount: cfg.BossGoldOption),
            new(StageRewardOptionType.StatBoost, AttackBonus: cfg.BossAttackBoost, HealthBonus: cfg.BossHealthBoost),
            new(StageRewardOptionType.Box, RuneChance: cfg.BossBoxRuneChance),
        },
        StageTier.MidBoss => new List<StageRewardOption>
        {
            new(StageRewardOptionType.Gold, GoldAmount: cfg.MidBossGoldOption),
            new(StageRewardOptionType.StatBoost, AttackBonus: cfg.MidBossAttackBoost, HealthBonus: cfg.MidBossHealthBoost),
            new(StageRewardOptionType.Box, RuneChance: cfg.MidBossBoxRuneChance),
        },
        _ => new List<StageRewardOption>
        {
            new(StageRewardOptionType.Gold, GoldAmount: cfg.RegularGoldOption),
            new(StageRewardOptionType.StatBoost, AttackBonus: cfg.RegularAttackBoost, HealthBonus: cfg.RegularHealthBoost),
            new(StageRewardOptionType.Box, RuneChance: cfg.RegularBoxRuneChance),
        },
    };

    /// <summary>CompleteStageVictory가 반환한 선택지 중 selectedIndex번째를 확정 적용한다.</summary>
    public void ResolveStageRewardChoice(StageRewardChoice choice, int selectedIndex, Random rng)
    {
        var option = choice.Options[selectedIndex];
        switch (option.Type)
        {
            case StageRewardOptionType.Gold:
                Currency.Add(CurrencyType.Gold, (int)(option.GoldAmount * (1 + RelicGoldGainBonus)));
                break;

            case StageRewardOptionType.StatBoost:
                foreach (var c in Party)
                    c.AddRunBonus(option.AttackBonus, option.HealthBonus);
                break;

            case StageRewardOptionType.Box:
                if (rng.NextDouble() < option.RuneChance)
                {
                    var element = RuneElements[rng.Next(RuneElements.Length)];
                    Inventory.ReceiveRune(new Rune(element));
                }
                else
                {
                    ApplyRelic(RelicCatalog.RollRandom(rng));
                }
                break;
        }
    }

    private void ApplyRelic(Relic relic)
    {
        Relics.Add(relic);
        switch (relic.Effect)
        {
            case RelicEffect.RetireTimeReduction:
                RelicRetireSpeedBonus += relic.Magnitude;
                break;
            case RelicEffect.GoldGainBoost:
                RelicGoldGainBonus += relic.Magnitude;
                break;
            case RelicEffect.VendingUpgradeDiscount:
                RelicUpgradeDiscountRatio = Math.Min(0.9, RelicUpgradeDiscountRatio + relic.Magnitude);
                break;
            case RelicEffect.DodgeChanceBoost:
                foreach (var c in Party)
                    c.AddRelicDodgeBonus(relic.Magnitude);
                break;
        }
    }

    public bool CanUnlockCharacterSlot => Party.Count < SlotConfig.MaxSlots;

    /// <summary>다음 캐릭터 슬롯(현재 인원+1번째) 해금에 필요한 골드. 3번째 슬롯부터 유료.</summary>
    public int NextCharacterSlotGoldCost()
    {
        int costIndex = Party.Count - SlotConfig.StartingSlots;
        var costs = SlotConfig.UnlockGoldCosts;
        return costIndex >= 0 && costIndex < costs.Count ? costs[costIndex] : int.MaxValue;
    }

    /// <summary>골드로 새 캐릭터 슬롯(장착 무기/방어구 없는 빈 아바타)을 해금한다. 골드 차감은 호출자 책임.</summary>
    public Character UnlockCharacterSlot(double metaBaseHealthBonus)
    {
        var character = new Character($"Hero{Party.Count + 1}", metaBaseHealthBonus);
        Party.Add(character);
        return character;
    }
}
