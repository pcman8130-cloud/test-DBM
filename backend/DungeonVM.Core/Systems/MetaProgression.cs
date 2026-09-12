using DungeonVM.Core.Balance;

namespace DungeonVM.Core.Systems;

/// <summary>영혼(Souls)으로 강화하는 런 간 영구 스킬 트리. 런 종료 시 정산된 Souls로 다음 런 전에 투자한다.</summary>
public sealed class MetaProgression
{
    private static MetaProgressionBalanceSection Config => BalanceProvider.Current.MetaProgression;

    public static int MaxLevel => Config.MaxLevel;

    public int RetireTimeReductionLevel { get; private set; }
    public int FirstRollTierBoostLevel { get; private set; }
    public int BaseStatBoostLevel { get; private set; }

    public int BankedSouls { get; private set; }

    public void BankSouls(int amount) => BankedSouls += amount;

    private static int LevelCost(int currentLevel) => Config.LevelCostBase + currentLevel * Config.LevelCostPerLevel;

    public bool TryUpgradeRetireTimeReduction()
    {
        if (RetireTimeReductionLevel >= MaxLevel) return false;
        int cost = LevelCost(RetireTimeReductionLevel);
        if (BankedSouls < cost) return false;
        BankedSouls -= cost;
        RetireTimeReductionLevel++;
        return true;
    }

    public bool TryUpgradeFirstRollTierBoost()
    {
        if (FirstRollTierBoostLevel >= MaxLevel) return false;
        int cost = LevelCost(FirstRollTierBoostLevel);
        if (BankedSouls < cost) return false;
        BankedSouls -= cost;
        FirstRollTierBoostLevel++;
        return true;
    }

    public bool TryUpgradeBaseStat()
    {
        if (BaseStatBoostLevel >= MaxLevel) return false;
        int cost = LevelCost(BaseStatBoostLevel);
        if (BankedSouls < cost) return false;
        BankedSouls -= cost;
        BaseStatBoostLevel++;
        return true;
    }

    /// <summary>리타이어 카운트다운 진행 속도 배율(레벨당 +15%).</summary>
    public double RetireSpeedMultiplier => 1.0 + RetireTimeReductionLevel * Config.RetireSpeedPerLevel;

    /// <summary>런 시작 첫 뽑기가 2티어로 나올 확률(레벨당 +10%p).</summary>
    public double FirstRollTierBoostChance => FirstRollTierBoostLevel * Config.FirstRollTierBoostPerLevel;

    /// <summary>전 캐릭터 기본 체력 가산치(레벨당 +10).</summary>
    public double BaseHealthBonus => BaseStatBoostLevel * Config.BaseHealthBonusPerLevel;
}
