using DungeonVM.Core.Enums;
using DungeonVM.Core.Models;

namespace DungeonVM.Core.Combat;

/// <summary>
/// 한 스테이지 웨이브의 실시간 전투 시뮬레이션. 헤드리스 시뮬레이터가 Tick을 반복 호출해 전투를 진행시킨다.
/// 웨이브 내 몬스터는 등장과 동시에 전원 필드에 존재하는 것으로 단순화하고, 파티는 생존한 첫 번째 몬스터에 집중 공격한다.
/// </summary>
public sealed class BattleField
{
    /// <summary>전열에서 동시에 교전 가능한 몬스터 수(화면상 근접 슬롯 병목을 근사).</summary>
    private const int EngagementCap = 3;

    private readonly IReadOnlyList<Character> _party;
    private readonly List<Monster> _monsters;
    private readonly Random _rng;

    private readonly Dictionary<Guid, double> _characterCooldowns = new();
    private readonly Dictionary<Guid, double> _monsterCooldowns = new();

    public double ElapsedSeconds { get; private set; }

    public BattleField(IReadOnlyList<Character> party, List<Monster> monsters, Random rng)
    {
        _party = party;
        _monsters = monsters;
        _rng = rng;

        foreach (var c in _party) _characterCooldowns[c.Id] = 0;
        foreach (var m in _monsters) _monsterCooldowns[m.Id] = 0;
    }

    public bool WaveCleared => _monsters.All(m => !m.IsAlive);

    private Monster? FocusTarget() => _monsters.FirstOrDefault(m => m.IsAlive);

    /// <summary>double retireSpeedMultiplier: 유물/스킬로 리타이어 시간이 단축된 경우 1보다 큰 값을 전달.</summary>
    public RunEndReason Tick(double dt, double retireSpeedMultiplier = 1.0)
    {
        ElapsedSeconds += dt;

        // 0) 성서(Bible) 장착 캐릭터는 매 틱 파티 전체를 회복시킨다(레벨 5/10/15 스킬 마일스톤이 힐량에 근사 반영됨).
        foreach (var healer in _party)
        {
            if (!healer.IsAlive) continue;
            if (healer.EquippedWeapon is not { Type: WeaponType.Bible } bible) continue;

            double healPerSecond = WeaponCatalog.HealPerSecondAtTier(bible.Type, bible.Tier);
            if (healPerSecond <= 0) continue;

            foreach (var ally in _party)
                ally.Heal(healPerSecond * dt);
        }

        // 1) 파티가 몬스터를 공격 (전열/후열 관계없이 전원이 focus target을 타격)
        foreach (var c in _party)
        {
            if (!c.IsAlive) continue;

            _characterCooldowns[c.Id] += dt;
            double interval = c.AttacksPerSecond > 0 ? 1.0 / c.AttacksPerSecond : double.MaxValue;

            while (_characterCooldowns[c.Id] >= interval)
            {
                _characterCooldowns[c.Id] -= interval;
                var target = FocusTarget();
                if (target is null) break;

                double dmg = DamageCalculator.ComputeDamage(c.AttackDamage, c.Element, target.Element);
                target.TakeDamage(dmg);
            }
        }

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
            double interval = m.AttacksPerSecond > 0 ? 1.0 / m.AttacksPerSecond : double.MaxValue;

            while (_monsterCooldowns[m.Id] >= interval)
            {
                _monsterCooldowns[m.Id] -= interval;

                bool dodged = _rng.NextDouble() < monsterTarget.DodgeChance;
                if (!dodged) monsterTarget.TakeDamage(m.Damage);
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
}
