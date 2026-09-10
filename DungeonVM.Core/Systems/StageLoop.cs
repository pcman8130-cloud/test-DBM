using DungeonVM.Core.Combat;
using DungeonVM.Core.Enums;
using DungeonVM.Core.Inventory;
using DungeonVM.Core.Models;

namespace DungeonVM.Core.Systems;

/// <summary>1~30 스테이지 진행, 웨이브 생성, 클리어 보상, 정비 페이즈 전환을 조율하는 최상위 오케스트레이터.</summary>
public sealed class StageLoop
{
    public const int MaxStage = 30;

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

    /// <summary>스테이지 클리어 처리: 보상 지급, 전원 자동 부활, 다음 스테이지로 진행 후 정비 페이즈 전환.</summary>
    public void CompleteStageVictory()
    {
        int goldReward = 30 + CurrentStage * 5;
        int gemsReward = CurrentStage % 5 == 0 ? 5 : 1;
        int soulsReward = 2 + CurrentStage / 3;

        Currency.Add(CurrencyType.Gold, goldReward);
        Currency.Add(CurrencyType.Gems, gemsReward);
        SoulsEarnedThisRun += soulsReward;

        foreach (var c in Party)
            c.ReviveNow();

        Phase = StagePhase.Maintenance;
        CurrentStage++;
    }
}
