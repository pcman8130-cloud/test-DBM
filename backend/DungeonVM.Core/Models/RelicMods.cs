namespace DungeonVM.Core.Models;

/// <summary>
/// 보유한 유물들의 효과를 합산한 수치(런 전체에 적용). 유물 정의(RelicDef)는 각자 자기 효과만 채우고, StageLoop가 보유 유물을
/// 모두 더해 Character/BattleField/StageLoop가 읽는다. 퍼센트는 0.1 = 10%이고, 음수는 감소다.
/// </summary>
public sealed class RelicMods
{
    /// <summary>아무 유물도 없을 때 쓰는 읽기 전용 기본값(절대 수정하지 말 것).</summary>
    public static readonly RelicMods None = new();

    // 공격
    public double AttackPct;                // 모든 무기 공격력 +%
    public double AttackSpeedPct;           // 모든 무기 공격속도 +%
    public double PhysicalAttackPct;        // 물리 무기(검·방패·단검·활) 공격력 +%
    public double PhysicalAttackSpeedPct;   // 물리 무기 공격속도 +%(음수 = 감소)
    public double MagicAttackPct;           // 마법 무기(지팡이·성서) 공격력 +%
    public double BossDamagePct;            // 보스에게 주는 피해 +%

    // 방어
    public double DamageTakenPct;           // 받는 피해 +%(음수 = 감소)
    public double BossDamageTakenPct;       // 보스에게 받는 피해 +%
    public double AoeDamageTakenPct;        // 적의 범위 공격 피해 +%
    public double MaxHealthFlat;            // 최대 체력 + (고정)
    public double MaxHealthPct;             // 최대 체력 +%
    public double Dodge;                    // 회피율 +
    public double StartShield;              // 전투 시작 시 보호막
    public double RegenPerSecond;           // 초당 체력 회복
    public double HealOnAttack;             // 공격 시 체력 회복

    // 경제/자판기
    public double GoldGainPct;              // 골드 획득량 +%
    public double GoldPerKill;              // 몬스터 처치 시 골드 +
    public double FreeRollChance;           // 자판기 뽑기 후 다음 뽑기가 무료일 확률

    // 스킬
    public double Cdr;                      // 스킬 쿨타임 감소
    public double MagicCdr;                 // 마법 무기 추가 쿨타임 감소
    public double SkillAmp;                 // 스킬 증폭(스킬 효과 크기 +%)
    public double SkillDamage;              // 스킬 피해 +%

    // 특수 효과
    public double MeleeAoeChance;           // 근접 무기 공격 시 범위피해 발동 확률
    public double MeleeAoeRatio;            // 그 범위피해의 공격력 배수
    public double ExtraAttackChance;        // 기본 공격 시 한 번 더 공격할 확률
    public bool FireAttacks;                // 모든 공격에 화염 속성 부여
    public double FireRuneDotMult = 1.0;    // 불 룬 장착 시 화염 도트 피해 배수
    public bool HolyAttacks;                // 룬 없이도 모든 공격에 흡혈 부여
    public bool BibleHealer;                // 성서가 기본공격 대신 공격력만큼 아군을 치유

    public void Add(RelicMods o)
    {
        AttackPct += o.AttackPct; AttackSpeedPct += o.AttackSpeedPct;
        PhysicalAttackPct += o.PhysicalAttackPct; PhysicalAttackSpeedPct += o.PhysicalAttackSpeedPct;
        MagicAttackPct += o.MagicAttackPct; BossDamagePct += o.BossDamagePct;
        DamageTakenPct += o.DamageTakenPct; BossDamageTakenPct += o.BossDamageTakenPct; AoeDamageTakenPct += o.AoeDamageTakenPct;
        MaxHealthFlat += o.MaxHealthFlat; MaxHealthPct += o.MaxHealthPct; Dodge += o.Dodge;
        StartShield += o.StartShield; RegenPerSecond += o.RegenPerSecond; HealOnAttack += o.HealOnAttack;
        GoldGainPct += o.GoldGainPct; GoldPerKill += o.GoldPerKill; FreeRollChance += o.FreeRollChance;
        Cdr += o.Cdr; MagicCdr += o.MagicCdr; SkillAmp += o.SkillAmp; SkillDamage += o.SkillDamage;
        MeleeAoeChance += o.MeleeAoeChance; MeleeAoeRatio = Math.Max(MeleeAoeRatio, o.MeleeAoeRatio);
        ExtraAttackChance += o.ExtraAttackChance;
        FireAttacks |= o.FireAttacks; FireRuneDotMult = Math.Max(FireRuneDotMult, o.FireRuneDotMult);
        HolyAttacks |= o.HolyAttacks; BibleHealer |= o.BibleHealer;
    }
}
