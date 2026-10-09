namespace DungeonVM.Core.Models;

/// <summary>
/// 모든 유물(보스 유물 + 일반 유물)의 정의 모음. 효과 수치는 각 RelicDef.Mods에 있고, 전투/경제에는 StageLoop가 합산한
/// RelicMods로 반영된다. prototype/index.html의 RELIC_DEFS와 같은 목록이다.
/// </summary>
public static class RelicCatalog
{
    private static RelicDef Boss(int stage, string id, string name, string desc, RelicMods mods, int goldOnPick = 0, bool bansWeapon = false)
        => new(id, name, desc, RelicKind.Boss, stage, mods, goldOnPick, bansWeapon);

    private static RelicDef Regular(int unlockStage, string id, string name, string desc, RelicMods mods)
        => new(id, name, desc, RelicKind.Regular, unlockStage, mods);

    public static readonly IReadOnlyList<RelicDef> All = new List<RelicDef>
    {
        // ── 5스테이지 ──
        Boss(5, "goblin_wallet", "탐욕의 고블린 지갑", "모든 골드 획득량 +25%, 받는 피해 +5%", new() { GoldGainPct = 0.25, DamageTakenPct = 0.05 }),
        Boss(5, "command_flag", "전투 지휘 깃발", "공격력 +10%, 공격속도 +10%", new() { AttackPct = 0.10, AttackSpeedPct = 0.10 }),
        Boss(5, "chief_epaulette", "족장의 견장", "받는 피해 -5%", new() { DamageTakenPct = -0.05 }),

        // ── 10스테이지 ──
        Boss(10, "gauntlet", "괴력의 건틀렛", "물리 무기(활·단검·검·방패) 공격력 +30%, 공격속도 -10%", new() { PhysicalAttackPct = 0.30, PhysicalAttackSpeedPct = -0.10 }),
        Boss(10, "orc_shaman", "오크 주술사의 축복", "성서가 기본공격 대신 공격력만큼 가장 체력이 낮은 아군을 치유", new() { BibleHealer = true }),
        Boss(10, "ogre_blood", "오우거의 피", "최대 체력 +50", new() { MaxHealthFlat = 50 }),

        // ── 15스테이지 ──
        Boss(15, "salamander_claw", "셀레멘더의 발톱", "모든 공격에 화염 속성 부여(불 룬 장착 시 화염 도트 피해 ×1.5)", new() { FireAttacks = true, FireRuneDotMult = 1.5 }),
        Boss(15, "mystic_cloak", "신비주의자의 망토", "회피율 +15%, 공격속도 +10%", new() { Dodge = 0.15, AttackSpeedPct = 0.10 }),
        Boss(15, "flame_tail", "불꽃 꼬리", "근접 무기(검·단검·방패) 공격 시 25% 확률로 공격력의 125% 범위 피해", new() { MeleeAoeChance = 0.25, MeleeAoeRatio = 1.25 }),

        // ── 20스테이지 ──
        Boss(20, "rune_tablet", "룬이 새겨진 석판", "스킬 쿨타임 -15%, 마법 무기(성서·지팡이) 쿨타임 -10% 추가 및 공격력 +15%", new() { Cdr = 0.15, MagicCdr = 0.10, MagicAttackPct = 0.15 }),
        Boss(20, "ancient_guardian", "고대의 수호자", "최대 체력 +20%, 적의 범위 공격 피해 -30%", new() { MaxHealthPct = 0.20, AoeDamageTakenPct = -0.30 }),
        Boss(20, "ancient_coin", "고대 주화", "100골드를 얻고, 무기 한 종류를 골라 이후 자판기·보상에서 나오지 않게 함", new(), goldOnPick: 100, bansWeapon: true),

        // ── 25스테이지 ──
        Boss(25, "dragon_orb", "용의 보주", "스킬 쿨타임 -20%, 스킬 피해 +20%", new() { Cdr = 0.20, SkillDamage = 0.20 }),
        Boss(25, "scale_wing", "비늘날개 장식", "공격속도 +10%, 기본 공격 시 20% 확률로 한 번 더 공격", new() { AttackSpeedPct = 0.10, ExtraAttackChance = 0.20 }),

        // ── 30스테이지(최종 보스) ──
        Boss(30, "crimson_grail", "진홍빛 성배", "모든 공격에 흡혈 효과가 자동으로 부여됨(룬 불필요)", new() { HolyAttacks = true }),

        // ── 일반 유물: 1~10스테이지부터 등장 ──
        Regular(1, "mercenary_shield", "용병단의 방패", "받는 피해 -5%", new() { DamageTakenPct = -0.05 }),
        Regular(1, "battle_horn", "전투나팔", "공격력 +5%", new() { AttackPct = 0.05 }),
        Regular(1, "shadow_cloak", "그림자 망토", "회피율 +5%", new() { Dodge = 0.05 }),
        Regular(1, "goblin_pouch", "고블린 주머니", "몬스터 처치 시 골드 +3", new() { GoldPerKill = 3 }),
        Regular(1, "harpy_feather", "하피의 깃털", "공격속도 +5%", new() { AttackSpeedPct = 0.05 }),

        // ── 일반 유물: 11스테이지부터 등장 ──
        Regular(11, "trick_coin", "속임수 동전", "자판기 뽑기 시 25% 확률로 다음 뽑기 무료", new() { FreeRollChance = 0.25 }),
        Regular(11, "first_aid_kit", "구급상자", "초당 체력 +1 회복", new() { RegenPerSecond = 1 }),
        Regular(11, "broken_hourglass", "부서진 모래시계", "스킬 쿨타임 -10%", new() { Cdr = 0.10 }),
        Regular(11, "bloodstone", "혈석", "공격 시 체력 3 회복", new() { HealOnAttack = 3 }),
        Regular(11, "giant_slayer", "거인 학살자", "보스에게 주는 피해 +10%", new() { BossDamagePct = 0.10 }),

        // ── 일반 유물: 21스테이지부터 등장 ──
        Regular(21, "expedition_badge", "원정대의 뱃지", "보스에게 받는 피해 -10%", new() { BossDamageTakenPct = -0.10 }),
        Regular(21, "scorched_shield", "그을린 고목 방패", "전투 시작 시 50의 보호막", new() { StartShield = 50 }),
        Regular(21, "cross_pendant", "십자가 펜던트", "스킬 효과 +10% 증폭", new() { SkillAmp = 0.10 }),
        Regular(21, "blood_epaulette", "핏빛 견장", "최대 체력 +25", new() { MaxHealthFlat = 25 }),
    };

    private static readonly Dictionary<string, RelicDef> ById = All.ToDictionary(r => r.Id);

    public static RelicDef? Get(string id) => ById.TryGetValue(id, out var def) ? def : null;

    /// <summary>해당 보스 스테이지(5·10·…·30)에서 고를 수 있는 보스 유물 후보.</summary>
    public static IReadOnlyList<RelicDef> BossCandidatesFor(int stage)
        => All.Where(r => r.Kind == RelicKind.Boss && r.UnlockStage == stage).ToList();

    /// <summary>클리어한 스테이지의 특수 상자에서 나올 수 있는 일반 유물 풀(UnlockStage ≤ 클리어 스테이지).</summary>
    public static IReadOnlyList<RelicDef> RegularPoolFor(int clearedStage)
        => All.Where(r => r.Kind == RelicKind.Regular && r.UnlockStage <= clearedStage).ToList();
}
