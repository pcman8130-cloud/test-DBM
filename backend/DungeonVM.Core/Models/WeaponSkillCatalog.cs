using DungeonVM.Core.Enums;

namespace DungeonVM.Core.Models;

/// <summary>무기별 Lv.5/10/15 스킬 마일스톤의 실제 전투 메커니즘(암살/기절/휩쓸기/광역/회복/속사).
/// 프로토타입(index.html)의 WEAPON_SKILLS 테이블과 정확히 동일한 수치다. WeaponCatalog.SkillBonusAt은
/// 이 스킬들과 별개로 모든 무기에 공통 적용되는 기본 전투력 보정치(프로토타입 SKILL_FLAT_BONUS와 동일)이고,
/// 이 클래스는 그 위에 무기 종류별로 추가로 얹히는 고유 효과를 담당한다. 백엔드는 예전엔 이 고유 효과 없이
/// SkillBonusAt 하나로만 모든 무기 타입을 뭉뚱그려서, 단검(암살)·활(속사)처럼 프로토타입에서 특히 강한
/// 타입의 실전 화력이 시뮬레이션에서 과소평가되고 있었다.</summary>
public static class WeaponSkillCatalog
{
    public static int SkillTierOf(int weaponTier) => weaponTier >= 15 ? 3 : weaponTier >= 10 ? 2 : weaponTier >= 5 ? 1 : 0;

    private static readonly double[] SwordCleaveRatio = { 0, 0.5, 0.75, 1.0 };
    private static readonly double[] ShieldStunChance = { 0, 0.25, 0.35, 0.50 };
    private static readonly double[] ShieldStunDuration = { 0, 1.0, 1.3, 1.6 };
    private static readonly double[] DaggerAssassinateChance = { 0, 0.20, 0.30, 0.40 };
    private static readonly double[] DaggerAssassinateMult = { 1, 2.0, 2.5, 3.2 };
    private static readonly double[] BowApsBonus = { 0, 0.4, 0.7, 1.1 };
    private static readonly double[] StaffAoeRatio = { 0, 0.6, 0.8, 1.0 };
    private static readonly double[] BibleHealRatio = { 0, 0.25, 0.4, 0.6 }; // 예전 0.5/0.8/1.2 — Lv.5만 찍어도 난이도가 너무 쉬워진다는 피드백으로 절반 수준

    public static double CleaveRatioAt(int weaponTier) => SwordCleaveRatio[SkillTierOf(weaponTier)];
    public static double StunChanceAt(int weaponTier) => ShieldStunChance[SkillTierOf(weaponTier)];
    public static double StunDurationAt(int weaponTier) => ShieldStunDuration[SkillTierOf(weaponTier)];
    public static double AssassinateChanceAt(int weaponTier) => DaggerAssassinateChance[SkillTierOf(weaponTier)];
    public static double AssassinateMultAt(int weaponTier) => DaggerAssassinateMult[SkillTierOf(weaponTier)];
    public static double AoeRatioAt(int weaponTier) => StaffAoeRatio[SkillTierOf(weaponTier)];
    public static double HealRatioAt(int weaponTier) => BibleHealRatio[SkillTierOf(weaponTier)];

    public static double ApsBonusFor(WeaponType type, int weaponTier)
        => type == WeaponType.Bow ? BowApsBonus[SkillTierOf(weaponTier)] : 0;
}
