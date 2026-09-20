using System.Text;
using System.Text.Json;

namespace DungeonVM.Simulator.Metrics;

/// <summary>
/// Supabase REST(PostgREST)로 런 로그를 적재하는 싱크. SUPABASE_URL / SUPABASE_SERVICE_KEY 환경 변수가
/// 없으면 IsConfigured=false가 되어 조용히 스킵한다(로컬 FileRunLogSink가 항상 기록을 보장).
/// </summary>
public sealed class SupabaseLogger : IRunLogSink
{
    private readonly HttpClient _http;
    private readonly string? _url;
    private readonly string? _serviceKey;
    private readonly string _table;

    public SupabaseLogger(HttpClient? http = null, string? url = null, string? serviceKey = null, string table = "run_logs")
    {
        _http = http ?? new HttpClient();
        _url = url ?? Environment.GetEnvironmentVariable("SUPABASE_URL");
        _serviceKey = serviceKey ?? Environment.GetEnvironmentVariable("SUPABASE_SERVICE_KEY");
        _table = table;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_url) && !string.IsNullOrWhiteSpace(_serviceKey);

    public async Task LogRunAsync(RunResult result, CancellationToken ct = default)
    {
        if (!IsConfigured) return;

        string endpoint = $"{_url!.TrimEnd('/')}/rest/v1/{_table}";
        var payload = new
        {
            bot_name = result.BotName,
            run_index = result.RunIndex,
            outcome = result.Outcome.ToString(),
            stages_cleared = result.StagesCleared,
            final_gold = result.FinalGold,
            final_souls = result.FinalSouls,
            final_weapons = result.FinalWeapons,
            final_armors = result.FinalArmors,
            grid_bottleneck_sells = result.GridBottleneckSells,
            rune_avoidance_skips = result.RuneAvoidanceSkips,
            savings_holds = result.SavingsHolds,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("apikey", _serviceKey);
        request.Headers.Add("Authorization", $"Bearer {_serviceKey}");
        request.Headers.Add("Prefer", "return=minimal");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}
