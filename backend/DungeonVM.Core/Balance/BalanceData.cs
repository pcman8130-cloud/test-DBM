namespace DungeonVM.Core.Balance;

/// <summary>
/// 게임의 모든 밸런스 수치를 담는 최상위 루트. 기본값은 임베디드 리소스 DefaultBalance.json에서 오며,
/// BalanceProvider.LoadFromFile/LoadFromJson으로 엑셀→JSON 파이프라인 산출물이나 대시보드 슬라이더 값을
/// 런타임에 주입할 수 있다. 이 클래스에 새 필드를 추가하면 DefaultBalance.json에도 값을 추가해야 한다.
/// </summary>
public sealed class BalanceData
{
    public WeaponBalanceSection Weapons { get; set; } = new();
    public ArmorBalanceSection Armor { get; set; } = new();
    public VendingMachineBalanceSection VendingMachine { get; set; } = new();
    public WaveBalanceSection Wave { get; set; } = new();
    public MetaProgressionBalanceSection MetaProgression { get; set; } = new();
    public CurrencyBalanceSection Currency { get; set; } = new();
    public StageLoopBalanceSection StageLoop { get; set; } = new();
    public CharacterBalanceSection Character { get; set; } = new();
    public CombatBalanceSection Combat { get; set; } = new();
    public MergeGridBalanceSection MergeGrid { get; set; } = new();
    public CharacterSlotBalanceSection CharacterSlots { get; set; } = new();
    public ElementEffectsBalanceSection ElementEffects { get; set; } = new();
    public StageRewardChoiceBalanceSection StageRewardChoice { get; set; } = new();
}

public sealed class WeaponStatEntry
{
    public string Row { get; set; } = "Front";
    public double BaseDamage { get; set; }
    public double AttacksPerSecond { get; set; }
    public double BonusHealth { get; set; }
    public double PullAggro { get; set; }
}

public sealed class WeaponBalanceSection
{
    public int MaxTier { get; set; } = 15;

    /// <summary>레벨 1~MaxTier의 데미지/힐 배율(1레벨=1.0 기준). 길이는 MaxTier와 같아야 한다.
    /// 10레벨을 "실질적 엔드스펙", 15레벨을 "극단적 하이롤"로 삼는 완만한 곡선.</summary>
    public List<double> LevelMultipliers { get; set; } = new();

    /// <summary>레벨 5/10/15(무기 스킬 해금 마일스톤) 도달 시 가산되는 전투력 보너스(근사 스킬 반영, 스택 누적).</summary>
    public double SkillBonusAtLevel5 { get; set; }
    public double SkillBonusAtLevel10 { get; set; }
    public double SkillBonusAtLevel15 { get; set; }

    public Dictionary<string, WeaponStatEntry> Table { get; set; } = new();
}

public sealed class ArmorRollRange
{
    public double MinHp { get; set; }
    public double MaxHp { get; set; }
    public double MinAtk { get; set; }
    public double MaxAtk { get; set; }
    public double MinDodge { get; set; }
    public double MaxDodge { get; set; }
}

public sealed class ArmorBalanceSection
{
    public double DodgeClampMax { get; set; } = 0.75;
    public Dictionary<string, ArmorRollRange> RollRanges { get; set; } = new();
}

public sealed class VendingMachineBalanceSection
{
    public int MaxUpgradeLevel { get; set; }
    public int WeaponRollCost { get; set; }
    public int ArmorRollCost { get; set; }
    public double Tier2ChanceBase { get; set; }
    public double Tier2ChancePerLevel { get; set; }
    public double LegendaryChanceBase { get; set; }
    public double LegendaryChancePerLevel { get; set; }
    public double EpicChanceBase { get; set; }
    public double EpicChancePerLevel { get; set; }
    public double RareChanceBase { get; set; }
    public double RareChancePerLevel { get; set; }
    public List<int> UpgradeGoldCosts { get; set; } = new();
}

public sealed class WaveBalanceSection
{
    public double ScaleBase { get; set; }
    public double ScalePerStage { get; set; }
    public int MobCountBase { get; set; }
    public int MobCountStageDivisor { get; set; }
    public double MobBaseHealth { get; set; }
    public double MobBaseDamage { get; set; }
    public double MobApsMin { get; set; }
    public double MobApsRandomRange { get; set; }
    public int MobGoldBase { get; set; }
    public int MobGoldStageDivisor { get; set; }
    public double MidBossHealth { get; set; }
    public double MidBossDamage { get; set; }
    public double MidBossAps { get; set; }
    public int MidBossGoldBase { get; set; }
    public int MidBossGoldPerStage { get; set; }
    public double BigBossHealth { get; set; }
    public double BigBossDamage { get; set; }
    public double BigBossAps { get; set; }
    public int BigBossGoldBase { get; set; }
    public int BigBossGoldPerStage { get; set; }
}

public sealed class MetaProgressionBalanceSection
{
    public int MaxLevel { get; set; }
    public int LevelCostBase { get; set; }
    public int LevelCostPerLevel { get; set; }
    public double RetireSpeedPerLevel { get; set; }
    public double FirstRollTierBoostPerLevel { get; set; }
    public double BaseHealthBonusPerLevel { get; set; }
}

public sealed class CurrencyBalanceSection
{
    public int WeaponMarketValueTierBase { get; set; }
    public Dictionary<string, int> ArmorMarketValues { get; set; } = new();
    public double SellRefundRatio { get; set; }
}

public sealed class StageLoopBalanceSection
{
    public int MaxStage { get; set; }
    public int VictoryGoldBase { get; set; }
    public int VictoryGoldPerStage { get; set; }
    public int VictorySoulsBase { get; set; }
    public int VictorySoulsStageDivisor { get; set; }
}

public sealed class CharacterBalanceSection
{
    public double BaseHealth { get; set; }
    public double RetireDurationSeconds { get; set; }
}

public sealed class CombatBalanceSection
{
    public double ElementAdvantageMultiplier { get; set; }
    public double ElementDisadvantageMultiplier { get; set; }
}

public sealed class MergeGridBalanceSection
{
    public int TotalCells { get; set; }
    public int CellsPerUnlock { get; set; }
    public List<int> UnlockGoldCosts { get; set; } = new();
}

public sealed class CharacterSlotBalanceSection
{
    public int StartingSlots { get; set; } = 2;
    public int MaxSlots { get; set; } = 5;

    /// <summary>3번째~MaxSlots번째 슬롯 해금 골드 비용 (길이 = MaxSlots - StartingSlots).</summary>
    public List<int> UnlockGoldCosts { get; set; } = new();
}

/// <summary>무기에 소켓된 속성 룬이 적중 시 발동하는 원소별 고유 효과. 정확한 수치는 밸런스 테스트로 확정 예정.</summary>
public sealed class ElementEffectsBalanceSection
{
    /// <summary>불: 화상(초당 도트 데미지, 지속시간), 다른 생존 몬스터에게도 SplashRatio 비율로 화상 전파(범위 도트).</summary>
    public double FireBurnDamagePerSecond { get; set; }
    public double FireBurnDurationSeconds { get; set; }
    public double FireSplashRatio { get; set; }

    /// <summary>얼음: 피격 대상의 공격속도를 일정 시간 SlowRatio 비율만큼 감소(둔화).</summary>
    public double IceSlowRatio { get; set; }
    public double IceSlowDurationSeconds { get; set; }

    /// <summary>독: 적중마다 중첩(최대 MaxStacks)되는 도트. 중첩 수에 비례해 초당 피해가 커진다.</summary>
    public double PoisonDamagePerStackPerSecond { get; set; }
    public double PoisonDurationSeconds { get; set; }
    public int PoisonMaxStacks { get; set; }

    /// <summary>전기: 원 피해량의 이 비율만큼 다른 생존 몬스터 1명에게 즉시 전이 피해.</summary>
    public double LightningChainDamageRatio { get; set; }

    /// <summary>빛: 원 피해량의 이 비율만큼 공격한 캐릭터를 즉시 회복(흡혈).</summary>
    public double HolyLifestealRatio { get; set; }

    /// <summary>어둠: 원 피해량의 이 비율만큼 같은 대상에게 추가 즉시 피해.</summary>
    public double DarkBonusDamageRatio { get; set; }
}

/// <summary>
/// 스테이지 클리어 시 기본보상 위에 추가로 제공되는 3개 선택보상(골드/팀 능력치 영구증가/상자)의 등급별 수치.
/// 중간보스(ST 5·15·25)·보스(ST 10·20·30)로 갈수록 강화된다. 상자는 RuneChance 확률로 룬, 나머지는 유물이 나온다.
/// </summary>
public sealed class StageRewardChoiceBalanceSection
{
    public int RegularGoldOption { get; set; }
    public double RegularAttackBoost { get; set; }
    public double RegularHealthBoost { get; set; }
    public double RegularBoxRuneChance { get; set; }

    /// <summary>중간보스 기본보상에 추가되는 영혼(정규 스테이지는 영혼을 주지 않는다).</summary>
    public int MidBossBaseSoulsBonus { get; set; }
    public int MidBossGoldOption { get; set; }
    public double MidBossAttackBoost { get; set; }
    public double MidBossHealthBoost { get; set; }
    public double MidBossBoxRuneChance { get; set; }

    public int BossBaseSoulsBonus { get; set; }
    public int BossGoldOption { get; set; }
    public double BossAttackBoost { get; set; }
    public double BossHealthBoost { get; set; }
    public double BossBoxRuneChance { get; set; }
}
