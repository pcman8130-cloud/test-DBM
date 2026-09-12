using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DungeonVM.LLM;

/// <summary>Claude API(Messages) 클라이언트. ANTHROPIC_API_KEY 환경 변수가 없으면 IsConfigured=false.</summary>
public sealed class AnthropicClient : ILlmClient
{
    private const string Endpoint = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    private readonly HttpClient _http;
    private readonly string? _apiKey;
    private readonly string _model;

    public AnthropicClient(HttpClient? http = null, string? apiKey = null, string model = "claude-sonnet-5")
    {
        _http = http ?? new HttpClient();
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        _model = model;
    }

    public string ProviderName => "Anthropic";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("ANTHROPIC_API_KEY가 설정되어 있지 않습니다.");

        var payload = new
        {
            model = _model,
            max_tokens = 2048,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = userPrompt } },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;
    }
}
