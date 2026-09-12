using DungeonVM.Core.Enums;
using DungeonVM.Core.Models;

namespace DungeonVM.Simulator.Metrics;

/// <summary>런 종료 시점의 장착 스냅샷을 누적해 무기 티어별/방어구 등급별 채택률을 집계한다.</summary>
public sealed class WeaponTierMetrics
{
    private readonly Dictionary<(WeaponType Type, int Tier), int> _weaponCounts = new();
    private readonly Dictionary<(ArmorType Type, ArmorRarity Rarity), int> _armorCounts = new();

    public int TotalWeaponObservations { get; private set; }
    public int TotalArmorObservations { get; private set; }

    public void RecordFinalEquip(IEnumerable<Character> party)
    {
        foreach (var c in party)
        {
            if (c.EquippedWeapon is { } w)
            {
                var key = (w.Type, w.Tier);
                _weaponCounts[key] = _weaponCounts.GetValueOrDefault(key) + 1;
                TotalWeaponObservations++;
            }

            if (c.EquippedArmor is { } a)
            {
                var key = (a.Type, a.Rarity);
                _armorCounts[key] = _armorCounts.GetValueOrDefault(key) + 1;
                TotalArmorObservations++;
            }
        }
    }

    /// <summary>여러 봇의 집계를 하나로 합쳐 게임 전체 관점의 채택률을 낼 때 사용한다(LLM 밸런싱 입력용).</summary>
    public void MergeFrom(WeaponTierMetrics other)
    {
        foreach (var (key, count) in other._weaponCounts)
            _weaponCounts[key] = _weaponCounts.GetValueOrDefault(key) + count;

        foreach (var (key, count) in other._armorCounts)
            _armorCounts[key] = _armorCounts.GetValueOrDefault(key) + count;

        TotalWeaponObservations += other.TotalWeaponObservations;
        TotalArmorObservations += other.TotalArmorObservations;
    }

    public IReadOnlyDictionary<(WeaponType Type, int Tier), int> WeaponEquipCounts => _weaponCounts;
    public IReadOnlyDictionary<(ArmorType Type, ArmorRarity Rarity), int> ArmorEquipCounts => _armorCounts;

    public double WeaponAdoptionRate(WeaponType type, int tier)
        => TotalWeaponObservations == 0 ? 0 : _weaponCounts.GetValueOrDefault((type, tier)) / (double)TotalWeaponObservations;

    public double ArmorAdoptionRate(ArmorType type, ArmorRarity rarity)
        => TotalArmorObservations == 0 ? 0 : _armorCounts.GetValueOrDefault((type, rarity)) / (double)TotalArmorObservations;
}
