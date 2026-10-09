using DungeonVM.Core.Balance;
using DungeonVM.Core.Enums;
using DungeonVM.Core.Models;
using DungeonVM.Core.Systems;

namespace DungeonVM.Core.Combat;

/// <summary>
/// 한 스테이지 웨이브의 실시간 전투 시뮬레이션. 헤드리스 시뮬레이터가 Tick을 반복 호출해 전투를 진행시킨다.
/// 웨이브 내 몬스터는 등장과 동시에 전원 필드에 존재하는 것으로 단순화하고, 파티는 생존한 첫 번째 몬스터에 집중 공격한다.
/// StageLoop.Mods(보유 유물 효과의 합)를 참조해 유물 효과를 반영한다(prototype/index.html의 RELIC_DEFS와 같은 목록).
/// </summary>
public sealed class BattleField
{
    /// <summary>전열에서 동시에 교전 가능한 몬스터 수(화면상 근접 슬롯 병목을 근사).</summary>
    private const int EngagementCap = 3;

    /// <summary>보스(중간/대형)의 주기적 특수기 간격(초). 웨이브 시작 5초 뒤 첫 발동, 이후 7초마다.</summary>
    private const double BossSkillInterval = 7.0;
    private const double BossSkillInitialDelay = 5.0;

    private readonly IReadOnlyList<Character> _party;
    private readonly List<Monster> _monsters;
    private readonly Random _rng;
    private readonly CurrencyManager _currency;
    private readonly StageLoop _stageLoop;

    private readonly Dictionary<Guid, double> _characterCooldowns = new();
    private readonly Dictionary<Guid, double> _monsterCooldowns = new();
    private readonly Dictionary<Guid, double> _monsterSpecialTimers = new();
    private readonly HashSet<Guid> _goldAwarded = new();

    // 속성 룬 적중 효과의 몬스터별 상태(화상/독/둔화). Monster 자체는 불변으로 유지하고 전투 중 상태만 여기서 추적한다.
    private readonly Dictionary<Guid, double> _monsterBurnDps = new();
    private readonly Dictionary<Guid, double> _monsterBurnRemaining = new();
    private readonly Dictionary<Guid, int> _monsterPoisonStacks = new();
    private readonly Dictionary<Guid, double> _monsterPoisonRemaining = new();
    private readonly Dictionary<Guid, double> _monsterSlowRemaining = new();

    public double ElapsedSeconds { get; private set; }

    public BattleField(IReadOnlyList<Character> party, List<Monster> monsters, Random rng, CurrencyManager currency, StageLoop stageLoop)
    {
        _party = party;
        _monsters = monsters;
        _rng = rng;
        _currency = currency;
        _stageLoop = stageLoop;

        foreach (var c in _party)
        {
            _characterCooldowns[c.Id] = 0;
            c.GrantShield(_stageLoop.Mods.StartShield); // 그을린 고목 방패: 전투 시작 시 보호막
        }
        foreach (var m in _monsters)
        {
            _monsterCooldowns[m.Id] = 0;
            if (m.IsMidBoss || m.IsBigBoss)
                _monsterSpecialTimers[m.Id] = BossSkillInitialDelay;
        }
    }

    public bool WaveCleared => _monsters.All(m => !m.IsAlive);

    private Monster? FocusTarget() => _monsters.FirstOrDefault(m => m.IsAlive);

    /// <summary>double retireSpeedMultiplier: 유물/스킬로 리타이어 시간이 단축된 경우 1보다 큰 값을 전달.</summary>
    public RunEndReason Tick(double dt, double retireSpeedMultiplier = 1.0)
    {
        ElapsedSeconds += dt;

        // 구급상자: 초당 체력 회복
        if (_stageLoop.Mods.RegenPerSecond > 0)
            foreach (var ally in _party)
                if (ally.IsAlive) ally.Heal(_stageLoop.Mods.RegenPerSecond * dt);

        // 1) 파티가 몬스터를 공격 (전열/후열 관계없이 전원이 focus target을 타격)
        // 성서의 광역 회복은 더 이상 매 틱 별도 처리하지 않는다 — 프로토타입과 동일하게 PerformCharacterAttack
        // 안에서 "공격 명중 시" 그 공격의 피해량에 비례해 회복하는 방식으로 통합했다(연속 초당 회복이 아님).
        foreach (var c in _party)
        {
            if (!c.IsAlive) continue;

            _characterCooldowns[c.Id] += dt;
            double interval = c.AttacksPerSecond > 0 ? 1.0 / c.AttacksPerSecond : double.MaxValue;

            while (_characterCooldowns[c.Id] >= interval)
            {
                _characterCooldowns[c.Id] -= interval;
                PerformCharacterAttack(c, isExtra: false);
            }
        }

        // 1.5) 화상/독 도트 및 둔화 지속시간 진행. 도트 피해로 인한 처치도 이번 틱의 웨이브 클리어 판정에 반영한다.
        TickStatusEffects(dt);

        if (WaveCleared)
            return RunEndReason.Victory;

        // 2) 몬스터가 전열 우선으로 캐릭터를 공격한다(자판기는 체력이 없어 공격받지 않음).
        // 동시 교전 가능 수를 EngagementCap으로 제한해 "전열 병목"을 표현한다(웨이브 전체가 한 캐릭터에 몰리지 않도록).
        var monsterTarget = FormationManager.SelectMonsterTarget(_party);
        var activeAttackers = _monsters.Where(m => m.IsAlive).Take(EngagementCap);
        foreach (var m in activeAttackers)
        {
            if (monsterTarget is null) break; // 방어선이 없다 = 이미 전멸, 계산할 대상이 없다.

            _monsterCooldowns[m.Id] += dt;
            double effectiveAps = EffectiveAttacksPerSecond(m);
            double interval = effectiveAps > 0 ? 1.0 / effectiveAps : double.MaxValue;

            while (_monsterCooldowns[m.Id] >= interval)
            {
                _monsterCooldowns[m.Id] -= interval;
                PerformMonsterAttack(m, monsterTarget);
            }

            // 중간/대형보스 주기적 특수기(강타/광역 강습) — prototype/index.html의 performBossSkill과 동일 스펙.
            if (m.IsAlive && (m.IsMidBoss || m.IsBigBoss))
            {
                _monsterSpecialTimers[m.Id] -= dt;
                if (_monsterSpecialTimers[m.Id] <= 0)
                {
                    _monsterSpecialTimers[m.Id] += BossSkillInterval;
                    PerformBossSkill(m);
                }
            }
        }

        // 3) 리타이어 타이머 진행 (스테이지 클리어 전 자연 부활 가능)
        foreach (var c in _party)
            c.AdvanceRetireTimer(dt, retireSpeedMultiplier);

        // 패배 조건은 출격한 모험가 전원이 동시에 전멸(리타이어)하는 경우뿐이다.
        if (FormationManager.PartyWiped(_party))
            return RunEndReason.PartyWiped;

        return RunEndReason.InProgress;
    }

    private void PerformCharacterAttack(Character c, bool isExtra)
    {
        var target = FocusTarget();
        if (target is null) return;

        var mods = _stageLoop.Mods;
        var weapon = c.EquippedWeapon;

        // 오크 주술사의 축복: 성서는 기본공격 대신 공격력만큼 가장 체력 비율이 낮은 아군을 치유한다(피해는 주지 않는다).
        if (mods.BibleHealer && weapon?.Type == WeaponType.Bible)
        {
            var wounded = _party.Where(a => a.IsAlive).OrderBy(a => a.CurrentHealth / a.MaxHealth).FirstOrDefault();
            wounded?.Heal(c.AttackDamage * (1 + mods.SkillAmp + mods.SkillDamage));
            return;
        }

        // 쿨타임 감소는 스킬 발동 확률 증가로 근사(마법 무기는 Character.AttacksPerSecond에서 시전 속도로 반영).
        // 스킬 증폭/스킬 피해는 스킬 효과 크기 배율.
        double skillChanceMult = 1 + c.CooldownReduction;
        double skillPowerMult = 1 + mods.SkillAmp + mods.SkillDamage;

        double dmg = DamageCalculator.ComputeDamage(c.AttackDamage, c.Element, target.Element);
        if (target.IsMidBoss || target.IsBigBoss) dmg *= 1 + mods.BossDamagePct;

        // 무기 고유 스킬(Lv.5/10/15 마일스톤) — 프로토타입 applyWeaponSkill과 동일 스펙.
        if (weapon is not null)
        {
            // 단검(암살): 명중 시 확률로 고배율 치명타. 이후 피해 계산(원소 효과 등)에 전부 반영된다.
            if (weapon.Type == WeaponType.Dagger
                && _rng.NextDouble() < WeaponSkillCatalog.AssassinateChanceAt(weapon.Tier) * skillChanceMult)
                dmg *= WeaponSkillCatalog.AssassinateMultAt(weapon.Tier) * skillPowerMult;
        }

        DamageMonster(target, dmg);
        ApplyElementEffect(c, target, dmg);
        if (mods.HealOnAttack > 0) c.Heal(mods.HealOnAttack); // 혈석

        if (weapon is not null)
        {
            // 검(휩쓸기)/지팡이(비전 폭발): 명중 시 다른 생존 몬스터에게도 피해량 비율만큼 추가 피해.
            double splashRatio = weapon.Type switch
            {
                WeaponType.Sword => WeaponSkillCatalog.CleaveRatioAt(weapon.Tier),
                WeaponType.Staff => WeaponSkillCatalog.AoeRatioAt(weapon.Tier),
                _ => 0,
            } * skillPowerMult;
            if (splashRatio > 0)
            {
                foreach (var other in _monsters)
                    if (other.Id != target.Id && other.IsAlive)
                        DamageMonster(other, dmg * splashRatio);
            }

            // 방패(밀치기): 명중 시 확률로 대상 몬스터의 다음 공격을 지연시킨다(쿨다운을 되돌림).
            // 보스(중간/대형)는 기절 면역 — 안 그러면 방패 하나로 보스 평타 딜레이를 계속 벌어 보스전이
            // 크게 무력화된다(일반 몹 상대로는 여전히 정상 적용).
            if (weapon.Type == WeaponType.Shield && !target.IsMidBoss && !target.IsBigBoss
                && _rng.NextDouble() < WeaponSkillCatalog.StunChanceAt(weapon.Tier) * skillChanceMult)
                _monsterCooldowns[target.Id] -= WeaponSkillCatalog.StunDurationAt(weapon.Tier) * skillPowerMult;

            // 성서(광역 회복): 명중 시 이번 공격의 피해량에 비례해 파티 전원을 회복(연속 초당 회복이 아니라 "공격당" 회복).
            double healRatio = (weapon.Type == WeaponType.Bible ? WeaponSkillCatalog.HealRatioAt(weapon.Tier) : 0) * skillPowerMult;
            if (healRatio > 0)
            {
                double healAmt = dmg * healRatio;
                foreach (var ally in _party)
                    if (ally.IsAlive) ally.Heal(healAmt);
            }
        }

        // 불꽃 꼬리: 근접 무기(검·단검·방패) 공격 시 확률로 공격력의 N배 범위 피해(살아 있는 모든 몬스터).
        if (mods.MeleeAoeChance > 0 && weapon is { Type: WeaponType.Sword or WeaponType.Dagger or WeaponType.Shield }
            && _rng.NextDouble() < mods.MeleeAoeChance)
        {
            foreach (var other in _monsters)
                if (other.IsAlive) DamageMonster(other, c.AttackDamage * mods.MeleeAoeRatio);
        }

        // 비늘날개 장식: 기본 공격 시 확률로 즉시 한 번 더 공격(무한 연쇄 방지를 위해 최초 공격에서만 발동).
        if (!isExtra && mods.ExtraAttackChance > 0 && _rng.NextDouble() < mods.ExtraAttackChance)
            PerformCharacterAttack(c, isExtra: true);
    }

    /// <summary>몬스터의 기본 평타. 회피 판정 후 적중하면 유물의 피해 감소를 적용한다.</summary>
    private void PerformMonsterAttack(Monster m, Character target)
    {
        if (_rng.NextDouble() < target.DodgeChance) return;
        target.TakeDamage(MitigateIncomingDamage(m.Damage, fromBoss: m.IsMidBoss || m.IsBigBoss, aoe: false));
    }

    /// <summary>중간보스=단일 대상 강타(1.8배), 대형보스=파티 전원 광역 강습(인당 0.7배). 회피와 유물 피해 감소만 적용된다.</summary>
    private void PerformBossSkill(Monster m)
    {
        if (m.IsMidBoss)
        {
            var target = FormationManager.SelectMonsterTarget(_party);
            if (target is null) return;
            if (_rng.NextDouble() < target.DodgeChance) return;
            target.TakeDamage(MitigateIncomingDamage(m.Damage * 1.8, fromBoss: true, aoe: false));
        }
        else if (m.IsBigBoss)
        {
            foreach (var c in _party)
            {
                if (!c.IsAlive || c.EquippedWeapon is null) continue;
                if (_rng.NextDouble() < c.DodgeChance) continue;
                c.TakeDamage(MitigateIncomingDamage(m.Damage * 0.7, fromBoss: true, aoe: true));
            }
        }
    }

    /// <summary>받는 피해 보정: 용병단의 방패·족장의 견장(받는 피해), 원정대의 뱃지(보스에게 받는 피해), 고대의 수호자(범위 공격 피해).</summary>
    private double MitigateIncomingDamage(double rawDamage, bool fromBoss, bool aoe)
    {
        var mods = _stageLoop.Mods;
        double mult = 1 + mods.DamageTakenPct + (fromBoss ? mods.BossDamageTakenPct : 0);
        if (aoe) mult *= 1 + mods.AoeDamageTakenPct;
        return rawDamage * Math.Max(0.1, mult);
    }

    private double EffectiveAttacksPerSecond(Monster m)
    {
        if (_monsterSlowRemaining.GetValueOrDefault(m.Id) <= 0)
            return m.AttacksPerSecond;

        double slowMultiplier = BalanceProvider.Current.ElementEffects.IceSlowRatio;
        return m.AttacksPerSecond * (1 - slowMultiplier);
    }

    /// <summary>
    /// 공격한 캐릭터의 무기에 소켓된 룬 속성에 따라 부가 효과를 적용한다(속성 상성 배율과는 별개로 항상 발동).
    /// 불=화상(범위 도트), 얼음=둔화, 독=중첩 도트, 전기=전이 피해, 빛=흡혈, 어둠=추가 피해.
    /// 셀레멘더의 발톱(모든 공격에 화염 부여, 불 룬이면 도트 ×1.5)/진홍빛 성배(룬 없이 흡혈) 유물을 가지면 룬 없이도 자동 발동한다.
    /// </summary>
    private void ApplyElementEffect(Character attacker, Monster target, double dmg)
    {
        var mods = _stageLoop.Mods;
        var element = attacker.Element;
        bool fireRelic = mods.FireAttacks;
        bool autoHoly = element == ElementType.None && mods.HolyAttacks;

        if (element == ElementType.None && !fireRelic && !autoHoly) return;

        var cfg = BalanceProvider.Current.ElementEffects;

        if (element == ElementType.Fire || fireRelic)
        {
            double dotMult = element == ElementType.Fire && fireRelic ? mods.FireRuneDotMult : 1.0;
            SetBurn(target.Id, cfg.FireBurnDamagePerSecond * dotMult, cfg.FireBurnDurationSeconds);
            if (cfg.FireSplashRatio > 0)
            {
                foreach (var other in _monsters)
                {
                    if (other.Id == target.Id || !other.IsAlive) continue;
                    SetBurn(other.Id, cfg.FireBurnDamagePerSecond * dotMult * cfg.FireSplashRatio, cfg.FireBurnDurationSeconds);
                }
            }
        }

        if (element == ElementType.Holy || autoHoly)
            attacker.Heal(dmg * cfg.HolyLifestealRatio);

        switch (element)
        {
            case ElementType.Ice:
                _monsterSlowRemaining[target.Id] = cfg.IceSlowDurationSeconds;
                break;

            case ElementType.Poison:
                int stacks = Math.Min(cfg.PoisonMaxStacks, _monsterPoisonStacks.GetValueOrDefault(target.Id) + 1);
                _monsterPoisonStacks[target.Id] = stacks;
                _monsterPoisonRemaining[target.Id] = cfg.PoisonDurationSeconds;
                break;

            case ElementType.Lightning:
                var chainCandidates = _monsters.Where(m => m.IsAlive && m.Id != target.Id).ToList();
                if (chainCandidates.Count > 0)
                {
                    var chainTarget = chainCandidates[_rng.Next(chainCandidates.Count)];
                    DamageMonster(chainTarget, dmg * cfg.LightningChainDamageRatio);
                }
                break;

            case ElementType.Dark:
                DamageMonster(target, dmg * cfg.DarkBonusDamageRatio);
                break;
        }
    }

    /// <summary>몬스터에게 피해를 주고, 이번 피해로 처치했다면 골드를 지급한다(몬스터당 1회만, 약탈자의 깃발 보너스 포함).</summary>
    private void DamageMonster(Monster m, double amount)
    {
        if (!m.IsAlive) return;

        m.TakeDamage(amount);
        if (!m.IsAlive && _goldAwarded.Add(m.Id))
        {
            bool isBossKill = m.IsMidBoss || m.IsBigBoss;
            _currency.Add(CurrencyType.Gold, (int)(m.GoldReward * _stageLoop.GoldGainMultiplier(isBossKill)) + (int)_stageLoop.Mods.GoldPerKill);
        }
    }

    private void SetBurn(Guid monsterId, double damagePerSecond, double durationSeconds)
    {
        _monsterBurnDps[monsterId] = Math.Max(_monsterBurnDps.GetValueOrDefault(monsterId), damagePerSecond);
        _monsterBurnRemaining[monsterId] = Math.Max(_monsterBurnRemaining.GetValueOrDefault(monsterId), durationSeconds);
    }

    private void TickStatusEffects(double dt)
    {
        var cfg = BalanceProvider.Current.ElementEffects;

        foreach (var m in _monsters)
        {
            if (!m.IsAlive) continue;

            if (_monsterBurnRemaining.GetValueOrDefault(m.Id) > 0)
            {
                DamageMonster(m, _monsterBurnDps[m.Id] * dt);
                _monsterBurnRemaining[m.Id] -= dt;
            }

            if (_monsterPoisonRemaining.GetValueOrDefault(m.Id) > 0)
            {
                DamageMonster(m, cfg.PoisonDamagePerStackPerSecond * _monsterPoisonStacks.GetValueOrDefault(m.Id) * dt);
                _monsterPoisonRemaining[m.Id] -= dt;
                if (_monsterPoisonRemaining[m.Id] <= 0)
                    _monsterPoisonStacks[m.Id] = 0;
            }

            if (_monsterSlowRemaining.GetValueOrDefault(m.Id) > 0)
                _monsterSlowRemaining[m.Id] -= dt;
        }
    }
}
