using System.Text;
using System.Text.Json;

namespace DungeonVM.LLM;

/// <summary>OpenAI Chat Completions(JSON Mode) 클라이언트. OPENAI_API_KEY 환경 변수가 없으면 IsConfigured=false.</summary>
public sealed class OpenAiClient : ILlmClient
{
    private const string Endpoint = "https://api.openai.com/v1/chat/completions";

    private readonly HttpClient _http;
    private readonly string? _apiKey;
    private readonly string _model;

    public OpenAiClient(HttpClient? http = null, string? apiKey = null, string model = "gpt-4o-mini")
    {
        _http = http ?? new HttpClient();
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        _model = model;
    }

    public string ProviderName => "OpenAI";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("OPENAI_API_KEY가 설정되어 있지 않습니다.");

        var payload = new
        {
            model = _model,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = systemPrompt + " 반드시 {\"items\": [...]} 형태의 최상위 JSON 객체로 응답하세요." },
                new { role = "user", content = userPrompt },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }
}
