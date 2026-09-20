using System.Text.Json;

namespace DungeonVM.Core.Balance;

/// <summary>
/// 모든 밸런스 수치의 단일 진입점. 기본값은 임베디드 리소스 DefaultBalance.json이며,
/// LoadFromFile/LoadFromJson으로 런타임에 전체 또는 일부 섹션만 덮어쓸 수 있다
/// (엑셀→JSON 파이프라인 산출물, 라이브 대시보드의 슬라이더 값 등).
/// 섹션 단위로만 교체되므로, override JSON에 없는 섹션은 이전 값을 그대로 유지한다.
/// </summary>
public static class BalanceProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static BalanceData? _current;

    public static BalanceData Current => _current ??= LoadDefault();

    public static BalanceData LoadDefault()
    {
        var asm = typeof(BalanceProvider).Assembly;
        using var stream = asm.GetManifestResourceStream("DungeonVM.Core.Balance.DefaultBalance.json")
            ?? throw new InvalidOperationException("임베디드 기본 밸런스 리소스(DefaultBalance.json)를 찾을 수 없습니다.");
        using var reader = new StreamReader(stream);
        string json = reader.ReadToEnd();

        var data = JsonSerializer.Deserialize<BalanceData>(json, JsonOptions)
            ?? throw new InvalidOperationException("기본 밸런스 JSON 파싱에 실패했습니다.");
        _current = data;
        return data;
    }

    public static BalanceData LoadFromFile(string path) => LoadFromJson(File.ReadAllText(path));

    public static BalanceData LoadFromJson(string json)
    {
        var baseline = _current ?? LoadDefault();
        var merged = CloneViaJson(baseline);

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("밸런스 JSON의 최상위는 object여야 합니다.");

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            string raw = prop.Value.GetRawText();
            switch (prop.Name.ToLowerInvariant())
            {
                case "weapons": merged.Weapons = Deserialize<WeaponBalanceSection>(raw, merged.Weapons); break;
                case "armor": merged.Armor = Deserialize<ArmorBalanceSection>(raw, merged.Armor); break;
                case "vendingmachine": merged.VendingMachine = Deserialize<VendingMachineBalanceSection>(raw, merged.VendingMachine); break;
                case "wave": merged.Wave = Deserialize<WaveBalanceSection>(raw, merged.Wave); break;
                case "metaprogression": merged.MetaProgression = Deserialize<MetaProgressionBalanceSection>(raw, merged.MetaProgression); break;
                case "currency": merged.Currency = Deserialize<CurrencyBalanceSection>(raw, merged.Currency); break;
                case "stageloop": merged.StageLoop = Deserialize<StageLoopBalanceSection>(raw, merged.StageLoop); break;
                case "character": merged.Character = Deserialize<CharacterBalanceSection>(raw, merged.Character); break;
                case "combat": merged.Combat = Deserialize<CombatBalanceSection>(raw, merged.Combat); break;
                case "mergegrid": merged.MergeGrid = Deserialize<MergeGridBalanceSection>(raw, merged.MergeGrid); break;
                case "characterslots": merged.CharacterSlots = Deserialize<CharacterSlotBalanceSection>(raw, merged.CharacterSlots); break;
                case "elementeffects": merged.ElementEffects = Deserialize<ElementEffectsBalanceSection>(raw, merged.ElementEffects); break;
                case "stagerewardchoice": merged.StageRewardChoice = Deserialize<StageRewardChoiceBalanceSection>(raw, merged.StageRewardChoice); break;
                // 알 수 없는 최상위 키는 무시한다(예: 파이프라인 메타데이터 필드).
            }
        }

        _current = merged;
        return merged;
    }

    public static void ResetToDefault() => _current = null;

    private static T Deserialize<T>(string json, T fallback)
        => JsonSerializer.Deserialize<T>(json, JsonOptions) ?? fallback;

    private static BalanceData CloneViaJson(BalanceData source)
    {
        string json = JsonSerializer.Serialize(source, JsonOptions);
        return JsonSerializer.Deserialize<BalanceData>(json, JsonOptions)!;
    }
}
