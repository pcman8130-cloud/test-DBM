using System.Reflection;
using System.Text.Json;
using DungeonVM.Core.Balance;

namespace DungeonVM.Simulator.Evaluation;

/// <summary>"섹션.필드" 경로로 BalanceData의 스칼라 수치를 읽고/쓰고, 바뀐 밸런스를 BalanceProvider에 설치한다.
/// 예외 경로: "weaponBaseDamage.Sword" → Weapons.Table["Sword"].BaseDamage.</summary>
internal static class BalanceMutator
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions JsonIndented = new() { WriteIndented = true };

    public static BalanceData Clone(BalanceData source)
        => JsonSerializer.Deserialize<BalanceData>(JsonSerializer.Serialize(source), Json)!;

    /// <summary>전체 섹션을 담은 JSON을 그대로 설치한다(LoadFromJson은 섹션 단위 교체라 부분 JSON이면 나머지 필드가 초기화되기 때문).</summary>
    public static void Install(BalanceData data) => BalanceProvider.LoadFromJson(JsonSerializer.Serialize(data));

    public static string ToJson(BalanceData data) => JsonSerializer.Serialize(data, JsonIndented);

    public static bool TryGet(BalanceData data, string path, out double value)
    {
        value = 0;
        return TryResolve(data, path, out var target, out var prop) && TryReadNumber(prop.GetValue(target), out value);
    }

    public static bool TrySet(BalanceData data, string path, double value, out string error)
    {
        error = "";
        if (!TryResolve(data, path, out var target, out var prop))
        {
            error = "경로를 찾을 수 없음";
            return false;
        }

        if (prop.PropertyType == typeof(int)) prop.SetValue(target, (int)Math.Round(value));
        else if (prop.PropertyType == typeof(double)) prop.SetValue(target, value);
        else
        {
            error = $"숫자 필드가 아님({prop.PropertyType.Name})";
            return false;
        }
        return true;
    }

    private static bool TryResolve(BalanceData data, string path, out object target, out PropertyInfo prop)
    {
        target = data;
        prop = null!;
        var parts = path.Split('.');
        if (parts.Length != 2) return false;

        if (parts[0].Equals("weaponBaseDamage", StringComparison.OrdinalIgnoreCase))
        {
            var key = data.Weapons.Table.Keys.FirstOrDefault(k => k.Equals(parts[1], StringComparison.OrdinalIgnoreCase));
            if (key is null) return false;
            target = data.Weapons.Table[key];
            return FindNumberProperty(target, nameof(WeaponStatEntry.BaseDamage), out prop);
        }

        var sectionProp = typeof(BalanceData).GetProperty(parts[0], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (sectionProp?.GetValue(data) is not { } section) return false;
        target = section;
        return FindNumberProperty(section, parts[1], out prop);
    }

    private static bool FindNumberProperty(object owner, string name, out PropertyInfo prop)
    {
        prop = owner.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)!;
        return prop is not null && prop.CanRead && prop.CanWrite
            && (prop.PropertyType == typeof(double) || prop.PropertyType == typeof(int));
    }

    private static bool TryReadNumber(object? raw, out double value)
    {
        value = 0;
        if (raw is double d) { value = d; return true; }
        if (raw is int i) { value = i; return true; }
        return false;
    }
}
