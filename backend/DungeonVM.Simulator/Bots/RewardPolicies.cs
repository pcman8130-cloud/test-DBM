using DungeonVM.Core.Combat;
using DungeonVM.Core.Enums;
using DungeonVM.Core.Models;
using DungeonVM.Core.Systems;

namespace DungeonVM.Simulator.Bots;

/// <summary>스테이지 클리어 때 3개 상자(장비/특수/능력치) 중 무엇을 열지 고르는 정책. choice.Options의 인덱스를 반환한다.</summary>
public delegate int RewardPolicy(BotContext ctx, StageRewardChoice choice);

/// <summary>
/// 봇이 상자를 고르는 방식 모음. 정책을 봇의 나머지 행동(저축/투자 성향)과 분리해 두어서,
/// 같은 경제 엔진(BalancedBot)에 정책만 바꿔 끼워 플레이 성향이 다른 봇을 만들 수 있다.
/// </summary>
public static class RewardPolicies
{
    /// <summary>다음에 싸울 스테이지가 중간보스/보스인가. 클리어 직후에는 CurrentStage가 이미 다음 스테이지다
    /// ("보스 직전 스테이지"에서 보상을 고르는 시점).</summary>
    public static bool NextStageIsBoss(BotContext ctx)
        => WaveEngine.IsMidBossStage(ctx.StageLoop.CurrentStage) || WaveEngine.IsBigBossStage(ctx.StageLoop.CurrentStage);

    /// <summary>장비 상자는 그리드에 빈 칸이 있어야 의미가 있다(꽉 차면 열어도 장비가 버려진다).</summary>
    private static bool EquipmentFits(BotContext ctx) => !ctx.Inventory.Grid.IsFull;

    /// <summary>장비 상자(그리드가 꽉 찼으면 능력치 상자로 대체).</summary>
    private static int Equipment(BotContext ctx, StageRewardChoice choice)
        => EquipmentFits(ctx) ? choice.IndexOf(StageRewardOptionType.EquipmentBox) : choice.IndexOf(StageRewardOptionType.StatBox);

    public static int Stat(BotContext ctx, StageRewardChoice choice) => choice.IndexOf(StageRewardOptionType.StatBox);
    public static int Special(BotContext ctx, StageRewardChoice choice) => choice.IndexOf(StageRewardOptionType.SpecialBox);

    /// <summary>항상 장비 상자 — 골드를 모아서 한꺼번에 쓰는 플레이에서는 장비 상자가 곧 추가 전력이다.</summary>
    public static int AlwaysEquipment(BotContext ctx, StageRewardChoice choice) => Equipment(ctx, choice);

    /// <summary>인원이 목표(4명)에 차기 전에는 새 캐릭터에게 줄 장비가 필요하니 장비 상자, 그 뒤로는 능력치 상자.</summary>
    public static int PartyFirstThenStat(BotContext ctx, StageRewardChoice choice)
        => ctx.Party.Count >= 4 ? Stat(ctx, choice) : Equipment(ctx, choice);

    /// <summary>보스 직전 스테이지에서만 특수 상자(룬/유물)를 열고, 그 외에는 fallback 정책을 따른다.</summary>
    public static RewardPolicy SpecialBeforeBoss(RewardPolicy fallback)
        => (ctx, choice) => NextStageIsBoss(ctx) ? Special(ctx, choice) : fallback(ctx, choice);
}

/// <summary>상자를 열어 나온 후보 3개 중 무엇을 고를지 정하는 규칙(2중 선택의 2단계).</summary>
public static class RewardCandidatePolicies
{
    /// <summary>기본 규칙. isSold: 이 봇이 절대 안 쓰고 파는 무기 종류(예: 지팡이)면 true를 돌려주는 함수.
    ///  - 장비 상자: 지금 쓸 만한 무기(안 파는 종류이고 파티 최저 무기 레벨 이상)가 있으면 무기, 아니면 지금 방어구보다 나은 방어구,
    ///    둘 다 아니거나 그리드가 가득 차 있으면 골드.
    ///  - 능력치 상자: 공격력% > 체력 > 공격속도 > 회피 > 쿨타임 감소 순.
    ///  - 특수 상자: 유물이 있으면 유물, 아니면 첫 룬.</summary>
    public static WeaponType DefaultBannedWeapon(BotContext ctx)
    {
        var used = ctx.Party.Where(p => p.EquippedWeapon is not null).Select(p => p.EquippedWeapon!.Type).ToHashSet();
        foreach (var t in new[] { WeaponType.Staff, WeaponType.Bible, WeaponType.Bow, WeaponType.Dagger, WeaponType.Sword, WeaponType.Shield })
            if (!used.Contains(t) && !ctx.StageLoop.BannedWeaponTypes.Contains(t)) return t;
        return WeaponType.Staff;
    }

    public static int Default(BotContext ctx, StageRewardReveal reveal, Func<WeaponType, bool>? isSold = null)
    {
        var c = reveal.Candidates;
        int Find(StageRewardOutcomeKind k) { for (int i = 0; i < c.Count; i++) if (c[i].Kind == k) return i; return -1; }

        switch (reveal.Box)
        {
            case StageRewardOptionType.EquipmentBox:
            {
                bool full = ctx.Inventory.Grid.IsFull;
                int w = Find(StageRewardOutcomeKind.Weapon), a = Find(StageRewardOutcomeKind.Armor), g = Find(StageRewardOutcomeKind.Gold);
                if (!full && w >= 0)
                {
                    var armed = ctx.Party.Where(p => p.EquippedWeapon is not null).Select(p => p.EquippedWeapon!.Tier).ToList();
                    int floor = armed.Count == 0 || ctx.Party.Any(p => p.EquippedWeapon is null) ? 0 : armed.Min();
                    if (isSold?.Invoke(c[w].Weapon!.Type) != true && c[w].Weapon!.Tier >= floor) return w;
                }
                if (!full && a >= 0)
                {
                    var worn = ctx.Party.Select(p => p.EquippedArmor?.Rarity ?? ArmorRarity.Common).ToList();
                    if (c[a].Armor!.Rarity > worn.Min()) return a;
                }
                return g >= 0 ? g : 0;
            }
            case StageRewardOptionType.SpecialBox:
            {
                int relic = Find(StageRewardOutcomeKind.Relic);
                return relic >= 0 ? relic : 0;
            }
            default:
            {
                foreach (var kind in new[] { StageRewardOutcomeKind.AttackPercent, StageRewardOutcomeKind.Health, StageRewardOutcomeKind.AttackSpeed,
                                             StageRewardOutcomeKind.Dodge, StageRewardOutcomeKind.CooldownReduction })
                {
                    int i = Find(kind);
                    if (i >= 0) return i;
                }
                return 0;
            }
        }
    }
}

/// <summary>구매 계획의 목표 종류.</summary>
public enum PlanGoalKind
{
    /// <summary>캐릭터 인원이 Target명이 될 때까지 슬롯을 산다.</summary>
    PartySize,
    /// <summary>무기 자판기(공격) 레벨이 Target이 될 때까지 올린다.</summary>
    AttackLevel,
    /// <summary>그리드 해금을 Target번 한다(시작 4칸 기준 4칸씩).</summary>
    GridUnlocks,
    /// <summary>구매가 아니라 마일스톤: 파티 전원의 무기 티어가 Target 이상이 될 때까지 재뽑기/합성에 골드를 쓴다.</summary>
    AllWeaponTier,
}

/// <summary>구매 계획 한 단계. 앞 단계가 끝나기 전에는 다음 단계로 넘어가지 않는다.
/// GiveUpStage 이후에도 못 채운 단계는 건너뛴다(도달 불가한 마일스톤에 영원히 막히지 않도록).</summary>
public sealed record PlanGoal(PlanGoalKind Kind, int Target, int GiveUpStage = int.MaxValue);

/// <summary>
/// BalancedBot의 성향 설정. SaveStagesAhead는 "목표 금액이 앞으로 몇 스테이지 수입 안에 모일 것 같으면 재뽑기를
/// 멈추고 저축할지"이다(0이면 저축 없이 번 돈을 바로바로 쓴다). Reward는 상자 선택 정책. Plan이 있으면
/// 기본의 "인원 우선 + 가장 싼 것부터" 구매 규칙 대신 이 계획의 순서대로만 구매한다(숙련자의 정해진 루트).
/// HoldGold가 켜져 있으면 계획의 현재 단계가 구매형일 때 목표 금액을 다 모을 때까지 재뽑기를 일절 하지 않는다
/// (SaveStagesAhead의 "곧 모일 때만 저축" 규칙 대신 무조건 모은다). OpeningWeapons는 첫 정비에서 무기를 이 개수까지 뽑는다.
/// AlwaysSell에 적힌 무기 종류는 뽑히는 즉시 판매하고 절대 장착하지 않는다. CullVendLevel(무기 자판기 레벨) 이상이 되면
/// 합성 짝이 없는 CullMaxLevel 이하 무기를 그리드에서 팔아 칸과 골드를 확보한다(사람이 하는 정리).
/// MakeRoomWhenFull이 켜져 있으면 그리드가 가득 찰 때 장착 중인 무기보다 낮은 레벨의 합성 짝 없는 무기를 팔아 칸을 만들고,
/// 그래도 가득 차 있고 골드가 남으면 그리드를 확장한다(사람은 가득 차면 뽑기를 멈추지 않고 정리/확장한다).
/// </summary>
public sealed record BalancedBotProfile(
    string Name, int SaveStagesAhead, RewardPolicy Reward, IReadOnlyList<PlanGoal>? Plan = null,
    bool HoldGold = false, int OpeningWeapons = 0,
    IReadOnlyList<WeaponType>? AlwaysSell = null, int CullVendLevel = 0, int CullMaxLevel = 0, bool MakeRoomWhenFull = false)
{
    /// <summary>밸런스 평가기가 기준으로 쓰는 기본 성향(기존 BalancedBot 그대로).</summary>
    public static BalancedBotProfile Default { get; } = new("Balanced", 3, RewardPolicies.PartyFirstThenStat);

    /// <summary>저축형: 목표 금액을 오래 모아서 한꺼번에 쓰고, 상자는 항상 장비 상자.</summary>
    public static BalancedBotProfile Hoarder { get; } = new("Hoarder", 6, RewardPolicies.AlwaysEquipment);

    /// <summary>즉시 강화형: 길게 모으지 않고(1스테이지 수입 안에 모일 때만 잠깐 저축) 번 돈을 바로 재뽑기/강화에 쓰고, 상자는 능력치 상자 위주로 열며
    /// 보스 직전 스테이지에서만 특수 상자를 연다(장비는 이미 바로바로 맞추고 있어서 장비 상자는 굳이).</summary>
    public static BalancedBotProfile Spender { get; } = new("Spender", 1, RewardPolicies.SpecialBeforeBoss(RewardPolicies.Stat));

    /// <summary>보스 대비형: 기본 균형 운영에, 보스 직전 스테이지에서는 특수 상자(룬/유물)로 갈아탄다.</summary>
    public static BalancedBotProfile BossPrep { get; } = new("BossPrep", 3, RewardPolicies.SpecialBeforeBoss(RewardPolicies.PartyFirstThenStat));

    /// <summary>숙련자 봇(개발자 본인 플레이 루트): 첫 골드로 무기 3개·방어구 2개를 뽑아 장착하고, 상자는 항상 능력치 상자를 고르며,
    /// 골드는 목표를 다 모을 때까지 재뽑기 없이 모은다. 구매 순서: 인원 3명(5스테이지 전) → 자판기 Lv.3(9스테이지쯤) →
    /// 그리드 1회 확장 → 무기 4레벨(10스테이지 전) → 인원 4명 → 파티 전원 무기 5레벨(20스테이지 전) → 자판기 Lv.4 → 인원 5명.
    /// 도달하지 못하는 마일스톤에 영원히 막히지 않도록 무기 레벨 단계는 기한(GiveUpStage)이 지나면 건너뛴다.
    /// 무기 정리(개발자 본인 습관): 지팡이는 나오는 즉시 팔고, 무기 자판기가 Lv.4가 되면 합성 짝이 없는 Lv.2 이하 무기는 판다.</summary>
    public static BalancedBotProfile Expert { get; } = new("Expert", 3, RewardPolicies.Stat, new PlanGoal[]
    {
        new(PlanGoalKind.PartySize, 3),
        new(PlanGoalKind.AttackLevel, 3),
        new(PlanGoalKind.GridUnlocks, 1),
        new(PlanGoalKind.AllWeaponTier, 4, GiveUpStage: 11),
        new(PlanGoalKind.PartySize, 4),
        new(PlanGoalKind.AllWeaponTier, 5, GiveUpStage: 22),
        new(PlanGoalKind.AttackLevel, 4),
        new(PlanGoalKind.PartySize, 5),
    }, HoldGold: true, OpeningWeapons: 3,
        AlwaysSell: new[] { WeaponType.Staff }, CullVendLevel: 4, CullMaxLevel: 2, MakeRoomWhenFull: true);

    /// <summary>숙련자 루트에서 상자만 항상 장비 상자로 고르는 변형. "능력치 상자를 안 골라도(무기 키우기 중심) 숙련자가 후반을 깰 수 있는가"를
    /// 재는 용도 — Expert(능력치 상자 위주)와 둘 다 후반에 도달해야 무기 성장이 핵심인 밸런스다.</summary>
    public static BalancedBotProfile ExpertWeapon { get; } = Expert with { Name = "ExpertWeapon", Reward = RewardPolicies.AlwaysEquipment };
}
