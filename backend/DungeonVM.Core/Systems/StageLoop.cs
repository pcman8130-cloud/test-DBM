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
        ElementType.Fire, ElementType.Ice, ElementType.Lightning, ElementType.Holy, ElementType.Dark,
    };

    private static StageLoopBalanceSection Config => BalanceProvider.Current.StageLoop;
    private static CharacterSlotBalanceSection SlotConfig => BalanceProvider.Current.CharacterSlots;
    private static RuneBalanceSection RuneConfig => BalanceProvider.Current.Rune;

    public static int MaxStage => Config.MaxStage;

    public int CurrentStage { get; private set; } = 1;
    public StagePhase Phase { get; private set; } = StagePhase.Maintenance;

    public CurrencyManager Currency { get; }
    public InventoryManager Inventory { get; }
    public VendingMachine Machine { get; } = new();
    public List<Character> Party { get; }

    public int SoulsEarnedThisRun { get; private set; }
    public bool IsRunComplete => CurrentStage > MaxStage;

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

    /// <summary>스테이지 클리어 처리: 보상 지급(+확률적 룬 드롭), 전원 자동 부활, 다음 스테이지로 진행 후 정비 페이즈 전환.</summary>
    public void CompleteStageVictory(Random rng)
    {
        var config = Config;
        int goldReward = config.VictoryGoldBase + CurrentStage * config.VictoryGoldPerStage;
        int soulsReward = config.VictorySoulsBase + CurrentStage / config.VictorySoulsStageDivisor;

        Currency.Add(CurrencyType.Gold, goldReward);
        SoulsEarnedThisRun += soulsReward;

        bool isBossStage = WaveEngine.IsMidBossStage(CurrentStage) || WaveEngine.IsBigBossStage(CurrentStage);
        double dropChance = isBossStage ? RuneConfig.BossStageDropChance : RuneConfig.NormalStageDropChance;
        if (rng.NextDouble() < dropChance)
        {
            var element = RuneElements[rng.Next(RuneElements.Length)];
            Inventory.ReceiveRune(new Rune(element));
        }

        foreach (var c in Party)
            c.ReviveNow();

        Phase = StagePhase.Maintenance;
        CurrentStage++;
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
