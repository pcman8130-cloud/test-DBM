using System.Text.Json;
using DungeonVM.Core.Balance;
using DungeonVM.LLM.Tuning;

namespace DungeonVM.Simulator.Evaluation;

/// <summary>BalanceTargets.json의 tunableParameters에서 읽은 "LLM이 건드려도 되는 파라미터와 허용 범위".
/// 이 화이트리스트를 통과하지 못한 제안은 시뮬레이션에 적용되기 전에 거부된다.</summary>
internal sealed class TunableWhitelist
{
    private readonly Dictionary<string, (string Path, double Min, double Max)> _entries = new(StringComparer.OrdinalIgnoreCase);

    public static TunableWhitelist Load(JsonElement targets)
    {
        var list = new TunableWhitelist();
        if (!targets.TryGetProperty("tunableParameters", out var tp) || tp.ValueKind != JsonValueKind.Object)
            return list;

        foreach (var section in tp.EnumerateObject())
        {
            if (section.Name.StartsWith('_') || section.Value.ValueKind != JsonValueKind.Object) continue;

            foreach (var param in section.Value.EnumerateObject())
            {
                if (param.Value.ValueKind != JsonValueKind.Object) continue;
                if (!param.Value.TryGetProperty("min", out var min) || !param.Value.TryGetProperty("max", out var max)) continue;

                string path = $"{section.Name}.{param.Name}";
                list._entries[path] = (path, min.GetDouble(), max.GetDouble());
            }
        }
        return list;
    }

    public int Count => _entries.Count;

    public IReadOnlyList<TunableParameter> Describe(BalanceData data)
        => _entries.Values
            .Select(e => BalanceMutator.TryGet(data, e.Path, out var cur) ? new TunableParameter(e.Path, cur, e.Min, e.Max) : null)
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();

    /// <summary>제안 1건을 검증한다. 통과하면 (true, 정규화된 path)를, 아니면 (false, 거부 사유)를 돌려준다.</summary>
    public (bool Ok, string Info) Validate(ParameterChange change, BalanceData current)
    {
        if (!_entries.TryGetValue(change.Path.Trim(), out var entry))
            return (false, "화이트리스트에 없는 경로");

        if (double.IsNaN(change.NewValue) || double.IsInfinity(change.NewValue))
            return (false, "숫자가 아닌 값");

        if (change.NewValue < entry.Min || change.NewValue > entry.Max)
            return (false, $"허용 범위 [{entry.Min:G6}, {entry.Max:G6}] 밖");

        if (!BalanceMutator.TryGet(current, entry.Path, out var cur))
            return (false, "현재 값을 읽을 수 없음");

        if (Math.Abs(cur - change.NewValue) < 1e-9)
            return (false, "현재 값과 동일(변화 없음)");

        return (true, entry.Path);
    }
}
