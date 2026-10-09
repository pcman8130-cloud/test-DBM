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

    /// <summary>이번 런에서 획득한 모든 유물(보스 유물 + 특수 상자 유물)의 Id. 중복 보유는 없다.</summary>
    public HashSet<string> OwnedRelicIds { get; } = new();

    /// <summary>보유 유물 효과의 합계(Character/BattleField가 읽는다).</summary>
    public RelicMods Mods { get; private set; } = new();

    public bool HasRelic(string id) => OwnedRelicIds.Contains(id);

    /// <summary>골드 획득 배율(몬스터 처치·스테이지 기본 보상). 보스 처치만 따로 더 주던 예전 규칙은 없어졌다.</summary>
    public double GoldGainMultiplier(bool isBossKill = false) => 1 + Mods.GoldGainPct;

    /// <summary>무기 한 종류를 이후 자판기·보상에서 나오지 않게 막는다(고대 주화). 이미 장착/보유 중인 무기는 그대로다.</summary>
    public void BanWeaponType(WeaponType type) => Machine.BannedWeaponTypes.Add(type);
    public IReadOnlyCollection<WeaponType> BannedWeaponTypes => Machine.BannedWeaponTypes;

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
    /// 다음 스테이지로 진행한다. 이어서 골라야 할 3개 상자(장비/특수/능력치)를 반환하므로,
    /// 호출자(봇 정책 또는 Unity UI)가 상자 하나를 골라 OpenRewardBox로 열고, 나온 후보 중 하나를 ClaimRewardCandidate로 확정해야 한다.
    /// </summary>
    public StageRewardChoice CompleteStageVictory(Random rng)
    {
        var config = Config;
        var rewardConfig = RewardConfig;

        int goldReward = config.VictoryGoldBase + CurrentStage * config.VictoryGoldPerStage;
        Currency.Add(CurrencyType.Gold, (int)(goldReward * GoldGainMultiplier()));

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

        var options = BuildRewardOptions(tier, rewardConfig, CurrentStage);

        foreach (var c in Party)
            c.ReviveNow();

        Phase = StagePhase.Maintenance;
        CurrentStage++;

        return new StageRewardChoice(tier, options);
    }

    /// <summary>3개 상자(장비/특수/능력치)의 수치를 만든다. 능력치 상자는 스테이지가 오를수록
    /// `기본값 + PerStage × stage`로 커진다. 상자 안에서 무엇이 나올지는 여기서 정하지 않고,
    /// 고른 뒤 OpenRewardBox에서 후보 3개를 굴려 미리 보여 준다(2중 선택).</summary>
    private List<StageRewardOption> BuildRewardOptions(StageTier tier, StageRewardChoiceBalanceSection cfg, int stage)
    {
        var (runeChance, attackPercent, healthBonus, utilityScale, goldAmount) = tier switch
        {
            StageTier.Boss => (cfg.BossBoxRuneChance,
                cfg.BossAttackPercent + cfg.BossAttackPercentPerStage * stage,
                cfg.BossHealthBoost + cfg.BossHealthBoostPerStage * stage,
                cfg.BossUtilityScale,
                (int)(cfg.BossGoldOption + cfg.BossGoldOptionPerStage * stage)),
            StageTier.MidBoss => (cfg.MidBossBoxRuneChance,
                cfg.MidBossAttackPercent + cfg.MidBossAttackPercentPerStage * stage,
                cfg.MidBossHealthBoost + cfg.MidBossHealthBoostPerStage * stage,
                cfg.MidBossUtilityScale,
                (int)(cfg.MidBossGoldOption + cfg.MidBossGoldOptionPerStage * stage)),
            _ => (cfg.RegularBoxRuneChance,
                cfg.RegularAttackPercent + cfg.RegularAttackPercentPerStage * stage,
                cfg.RegularHealthBoost + cfg.RegularHealthBoostPerStage * stage,
                1.0,
                (int)(cfg.RegularGoldOption + cfg.RegularGoldPerStage * stage)),
        };

        // 장비 상자 품질은 5스테이지 구간 표(EquipmentBoxBands)를 따른다. 보스 스테이지(5·10·…)는 표 그대로,
        // 일반 스테이지는 다음 보스 스테이지 표를 기준으로 무기 최소 레벨을 1 낮추고(그 레벨이 나올 확률을
        // RegularStageLowestLevelChance로 따로 줌) 방어구는 Rare/Epic만 나온다.
        var band = PickEquipmentBand(cfg, stage);
        bool bossStage = tier != StageTier.Regular;
        int minLevel = bossStage ? band.WeaponMinLevel : Math.Max(1, band.WeaponMinLevel - 1);
        minLevel = Math.Clamp(minLevel, 1, WeaponCatalog.MaxTier);
        int maxLevel = Math.Clamp(band.WeaponMaxLevel, minLevel, WeaponCatalog.MaxTier);
        double lowestChance = bossStage ? 0 : cfg.RegularStageLowestLevelChance;
        double epicChance = bossStage ? band.BossArmorEpicChance : band.RegularArmorEpicChance;
        double legendaryChance = bossStage ? band.BossArmorLegendaryChance : 0;

        return new List<StageRewardOption>
        {
            new(StageRewardOptionType.EquipmentBox,
                WeaponMinTier: minLevel, WeaponMaxTier: maxLevel, WeaponLowestChance: lowestChance,
                ArmorEpicChance: epicChance, ArmorLegendaryChance: legendaryChance, GoldAmount: goldAmount),
            new(StageRewardOptionType.SpecialBox, RuneChance: runeChance),
            new(StageRewardOptionType.StatBox,
                AttackPercent: attackPercent,
                HealthBonus: healthBonus,
                DodgeBonus: cfg.UtilityDodgeBoost * utilityScale,
                AttackSpeedBonus: cfg.UtilityAttackSpeedBoost * utilityScale,
                CooldownReduction: cfg.UtilityCooldownReduction * utilityScale),
        };
    }

    /// <summary>스테이지에 쓸 장비 상자 구간: BossStage ≥ 스테이지인 첫 구간(없으면 마지막 구간).</summary>
    private static EquipmentBoxBand PickEquipmentBand(StageRewardChoiceBalanceSection cfg, int stage)
    {
        var bands = cfg.EquipmentBoxBands;
        foreach (var band in bands.OrderBy(x => x.BossStage))
            if (band.BossStage >= stage) return band;
        return bands.OrderBy(x => x.BossStage).Last();
    }

    /// <summary>CompleteStageVictory가 반환한 상자 중 boxIndex번째를 열어 후보 3개를 굴려 미리 보여 준다(2중 선택의 2단계).
    /// 아직 아무것도 받지 않는다 — 플레이어가 후보 중 하나를 골라 ClaimRewardCandidate로 확정한다.
    ///  - 장비 상자: 무기 1개(종류·레벨 확정) / 방어구 1개(종류·등급 확정) / 골드.
    ///  - 특수 상자: 룬 또는 유물 3개(각각 RuneChance 확률로 룬, 서로 다른 것).
    ///  - 능력치 상자: 공격력%·체력·회피율·공격속도·쿨타임 감소 중 서로 다른 3개.</summary>
    public StageRewardReveal OpenRewardBox(StageRewardChoice choice, int boxIndex, Random rng)
    {
        var option = choice.Options[boxIndex];
        var candidates = option.Type switch
        {
            StageRewardOptionType.EquipmentBox => RevealEquipment(option, rng),
            StageRewardOptionType.SpecialBox => RevealSpecial(option, rng),
            _ => RevealStat(option, rng),
        };
        return new StageRewardReveal(option.Type, candidates);
    }

    /// <summary>후보 중 candidateIndex번째를 확정 적용한다.</summary>
    public StageRewardOutcome ClaimRewardCandidate(StageRewardReveal reveal, int candidateIndex)
    {
        var c = reveal.Candidates[candidateIndex];
        switch (c.Kind)
        {
            case StageRewardOutcomeKind.Weapon:
                return new StageRewardOutcome(c.Kind, c.Description, Inventory.ReceiveWeapon(c.Weapon!));
            case StageRewardOutcomeKind.Armor:
                return new StageRewardOutcome(c.Kind, c.Description, Inventory.ReceiveArmor(c.Armor!));
            case StageRewardOutcomeKind.Gold:
                Currency.Add(CurrencyType.Gold, c.GoldAmount);
                break;
            case StageRewardOutcomeKind.Rune:
                Inventory.ReceiveRune(new Rune(c.RuneElement!.Value));
                break;
            case StageRewardOutcomeKind.Relic:
                GrantRelic(c.Relic!);
                break;
            case StageRewardOutcomeKind.AttackPercent:
                foreach (var m in Party) m.AddRunAttackPercent(c.Amount);
                break;
            case StageRewardOutcomeKind.Health:
                foreach (var m in Party) m.AddRunBonus(0, c.Amount);
                break;
            case StageRewardOutcomeKind.Dodge:
                foreach (var m in Party) m.AddRunDodgeBonus(c.Amount);
                break;
            case StageRewardOutcomeKind.AttackSpeed:
                foreach (var m in Party) m.AddRunAttackSpeedBonus(c.Amount);
                break;
            case StageRewardOutcomeKind.CooldownReduction:
                foreach (var m in Party) m.AddRunCooldownReduction(c.Amount);
                break;
        }
        return new StageRewardOutcome(c.Kind, c.Description);
    }

    private List<StageRewardCandidate> RevealEquipment(StageRewardOption option, Random rng)
    {
        var weaponTypes = ((WeaponType[])Enum.GetValues(typeof(WeaponType))).Where(t => !Machine.BannedWeaponTypes.Contains(t)).ToArray();
        int level = option.WeaponMinTier;
        if (option.WeaponMaxTier > option.WeaponMinTier)
            level = option.WeaponLowestChance > 0
                ? (rng.NextDouble() < option.WeaponLowestChance ? option.WeaponMinTier : rng.Next(option.WeaponMinTier + 1, option.WeaponMaxTier + 1))
                : rng.Next(option.WeaponMinTier, option.WeaponMaxTier + 1);
        var weapon = new Weapon(weaponTypes[rng.Next(weaponTypes.Length)], level);

        var armorTypes = (ArmorType[])Enum.GetValues(typeof(ArmorType));
        double roll = rng.NextDouble();
        var rarity = roll < option.ArmorLegendaryChance ? ArmorRarity.Legendary
            : roll < option.ArmorLegendaryChance + option.ArmorEpicChance ? ArmorRarity.Epic
            : ArmorRarity.Rare;
        var armor = Armor.RollRandom(rng, armorTypes[rng.Next(armorTypes.Length)], rarity);

        int gold = (int)(option.GoldAmount * (1 + Mods.GoldGainPct));
        return new List<StageRewardCandidate>
        {
            new(StageRewardOutcomeKind.Weapon, $"{weapon.Type} Lv.{weapon.Tier}", Weapon: weapon),
            new(StageRewardOutcomeKind.Armor, $"{armor.Type} {armor.Rarity}", Armor: armor),
            new(StageRewardOutcomeKind.Gold, $"{gold}G", GoldAmount: gold),
        };
    }

    private List<StageRewardCandidate> RevealSpecial(StageRewardOption option, Random rng)
    {
        // 클리어한 스테이지까지 해금된 일반 유물 중 아직 안 가진 것만 후보가 된다(CurrentStage는 이미 다음 스테이지).
        var relicPool = RelicCatalog.RegularPoolFor(CurrentStage - 1).Where(r => !OwnedRelicIds.Contains(r.Id)).ToList();

        var list = new List<StageRewardCandidate>();
        var runes = new HashSet<ElementType>();
        var relics = new HashSet<string>();
        for (int guard = 0; list.Count < 3 && guard < 200; guard++)
        {
            bool wantRune = relicPool.Count == 0 || rng.NextDouble() < option.RuneChance;
            if (wantRune)
            {
                var element = RuneElements[rng.Next(RuneElements.Length)];
                if (runes.Add(element))
                    list.Add(new(StageRewardOutcomeKind.Rune, $"{element} 룬", RuneElement: element));
            }
            else
            {
                var relic = relicPool[rng.Next(relicPool.Count)];
                if (relics.Add(relic.Id))
                    list.Add(new(StageRewardOutcomeKind.Relic, relic.Name, Relic: relic));
                else if (relics.Count >= relicPool.Count)
                    relicPool.Clear(); // 풀을 다 썼으면 남는 칸은 룬으로 채운다
            }
        }
        return list;
    }

    private List<StageRewardCandidate> RevealStat(StageRewardOption option, Random rng)
    {
        var all = new List<StageRewardCandidate>
        {
            new(StageRewardOutcomeKind.AttackPercent, $"공격력 +{option.AttackPercent:P1}", Amount: option.AttackPercent),
            new(StageRewardOutcomeKind.Health, $"체력 +{option.HealthBonus:0.#}", Amount: option.HealthBonus),
            new(StageRewardOutcomeKind.Dodge, $"회피율 +{option.DodgeBonus:P1}p", Amount: option.DodgeBonus),
            new(StageRewardOutcomeKind.AttackSpeed, $"공격속도 +{option.AttackSpeedBonus:P1}", Amount: option.AttackSpeedBonus),
            new(StageRewardOutcomeKind.CooldownReduction, $"쿨타임 -{option.CooldownReduction:P1}", Amount: option.CooldownReduction),
        };
        for (int i = all.Count - 1; i > 0; i--) // Fisher-Yates 후 앞 3개
        {
            int j = rng.Next(i + 1);
            (all[i], all[j]) = (all[j], all[i]);
        }
        return all.Take(3).ToList();
    }

    /// <summary>유물을 획득한다(중복은 무시). 일회성 효과(골드)를 즉시 지급하고 유물 효과 합계를 다시 계산한다.
    /// 무기 금지(고대 주화)는 어떤 무기를 막을지 호출자가 BanWeaponType으로 따로 정한다.</summary>
    public void GrantRelic(RelicDef relic)
    {
        if (!OwnedRelicIds.Add(relic.Id)) return;
        if (relic.GoldOnPick > 0) Currency.Add(CurrencyType.Gold, relic.GoldOnPick);
        RecomputeRelicMods();
    }

    /// <summary>보스 유물 확정 선택을 적용한다(Id로).</summary>
    public void GrantBossRelic(string id)
    {
        if (RelicCatalog.Get(id) is { } def) GrantRelic(def);
    }

    private void RecomputeRelicMods()
    {
        var sum = new RelicMods();
        foreach (var id in OwnedRelicIds)
            if (RelicCatalog.Get(id) is { } def) sum.Add(def.Mods);
        Mods = sum;
        foreach (var c in Party) c.SetRelicMods(Mods);
    }

    public bool CanUnlockCharacterSlot => Party.Count < SlotConfig.MaxSlots;

    /// <summary>다음 캐릭터 슬롯(현재 인원+1번째) 해금에 필요한 골드. 3번째 슬롯부터 유료.</summary>
    public int NextCharacterSlotGoldCost()
    {
        int costIndex = Party.Count - SlotConfig.StartingSlots;
        var costs = SlotConfig.UnlockGoldCosts;
        return costIndex >= 0 && costIndex < costs.Count ? costs[costIndex] : int.MaxValue;
    }

    /// <summary>골드로 새 캐릭터 슬롯(장착 무기/방어구 없는 빈 아바타)을 해금한다. 골드 차감은 호출자 책임.
    /// 이미 획득한 유물 효과를 새 캐릭터에도 그대로 적용한다.</summary>
    public Character UnlockCharacterSlot(double metaBaseHealthBonus)
    {
        var character = new Character($"Hero{Party.Count + 1}", metaBaseHealthBonus);
        character.SetRelicMods(Mods);
        Party.Add(character);
        return character;
    }
}
