using System.Globalization;
using System.Text.Json;

namespace DungeonVM.LLM.Tuning;

/// <summary>LLM 응답(JSON 객체 {"changes":[...]} 또는 배열, 코드펜스 허용)을 ParameterChange 목록으로 변환한다.</summary>
public static class TuningProposalParser
{
    public static IReadOnlyList<ParameterChange> Parse(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(StripFence(raw));

            var result = new List<ParameterChange>();
            foreach (var el in Flatten(doc.RootElement))
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                string path = el.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
                string reasoning = el.TryGetProperty("reasoning", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? "" : "";
                if (path.Length == 0 || !TryReadNumber(el, "newValue", out double value)) continue;
                result.Add(new ParameterChange(path, value, reasoning));
            }
            return result;
        }
        catch (JsonException)
        {
            return Array.Empty<ParameterChange>();
        }
    }

    /// <summary>{"changes":[...]}, [...], [{"changes":[...]}] 세 가지 모양을 모두 변경 항목 나열로 펼친다
    /// (일부 모델이 객체를 배열로 한 겹 더 감싸서 응답하는 경우를 실측으로 확인함).</summary>
    private static IEnumerable<JsonElement> Flatten(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("changes", out var c) && c.ValueKind == JsonValueKind.Array)
                foreach (var e in c.EnumerateArray()) yield return e;
            yield break;
        }

        if (root.ValueKind != JsonValueKind.Array) yield break;

        foreach (var el in root.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("changes", out var inner) && inner.ValueKind == JsonValueKind.Array)
                foreach (var e in inner.EnumerateArray()) yield return e;
            else
                yield return el;
        }
    }

    private static bool TryReadNumber(JsonElement el, string name, out double value)
    {
        value = 0;
        if (!el.TryGetProperty(name, out var v)) return false;
        if (v.ValueKind == JsonValueKind.Number) return v.TryGetDouble(out value);
        return v.ValueKind == JsonValueKind.String
            && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string StripFence(string raw)
    {
        raw = raw.Trim();
        if (!raw.StartsWith("```")) return raw;

        int firstNewline = raw.IndexOf('\n');
        int lastFence = raw.LastIndexOf("```", StringComparison.Ordinal);
        if (firstNewline < 0 || lastFence <= firstNewline) return raw;
        return raw[(firstNewline + 1)..lastFence].Trim();
    }
}
