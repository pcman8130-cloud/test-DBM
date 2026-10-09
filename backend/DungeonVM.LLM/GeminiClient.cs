using System.Net;
using System.Text;
using System.Text.Json;

namespace DungeonVM.LLM;

/// <summary>
/// Google Gemini(generateContent) 클라이언트. GEMINI_API_KEY(또는 GOOGLE_API_KEY) 환경 변수가 없으면 IsConfigured=false.
/// 무료 플랜은 수요가 몰리면 503이 자주 나므로, 일시 오류(429/5xx)는 재시도하고 그래도 안 되면 다음 모델로 넘어간다.
/// 모델 이름은 자주 바뀌고 구버전은 신규 사용자에게 닫히므로 GEMINI_MODEL 환경 변수로 하나를 고정할 수 있다.
/// 키는 쿼리 파라미터로 전달하므로 URL을 절대 예외/로그 메시지에 넣지 않는다.
/// </summary>
public sealed class GeminiClient : ILlmClient
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/";
    private const int AttemptsPerModel = 2;

    private static readonly string[] DefaultModels = { "gemini-3.5-flash", "gemini-3.1-flash-lite", "gemini-flash-latest" };

    private readonly HttpClient _http;
    private readonly string? _apiKey;
    private readonly IReadOnlyList<string> _models;
    private readonly TimeSpan _retryDelay;

    public GeminiClient(HttpClient? http = null, string? apiKey = null, IReadOnlyList<string>? models = null, TimeSpan? retryDelay = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        _apiKey = apiKey
            ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY");

        string? pinned = Environment.GetEnvironmentVariable("GEMINI_MODEL");
        _models = models ?? (string.IsNullOrWhiteSpace(pinned) ? DefaultModels : new[] { pinned.Trim() });
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(3);
    }

    public string ProviderName => "Gemini";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("GEMINI_API_KEY가 설정되어 있지 않습니다.");

        var payload = new
        {
            systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = userPrompt } } } },
            generationConfig = new { responseMimeType = "application/json", maxOutputTokens = 4096 },
        };
        string body = JsonSerializer.Serialize(payload);

        var failures = new List<string>();
        foreach (string model in _models)
        {
            for (int attempt = 1; attempt <= AttemptsPerModel; attempt++)
            {
                var (status, text, error) = await SendOnceAsync(model, body, ct).ConfigureAwait(false);
                if (text is not null) return text;

                failures.Add($"{model}: {error}");
                // 키 자체가 무효(401/403)면 다른 모델로 바꿔도 같은 결과이므로 바로 중단한다.
                if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new LlmAuthException($"Gemini 인증 실패 — API 키가 잘못되었거나 폐기되었습니다. ({error})");
                if (!IsTransient(status)) break; // 404(구버전 모델)/400/403 등은 같은 모델로 재시도해도 소용없음 → 다음 모델
                if (attempt < AttemptsPerModel) await Task.Delay(_retryDelay, ct).ConfigureAwait(false);
            }
        }

        throw new HttpRequestException("Gemini 호출 실패 — " + string.Join(" | ", failures));
    }

    private async Task<(HttpStatusCode? Status, string? Text, string Error)> SendOnceAsync(string model, string body, CancellationToken ct)
    {
        string url = $"{BaseUrl}{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(_apiKey!)}";
        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(url, content, ct).ConfigureAwait(false);
            string raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return (response.StatusCode, null, $"HTTP {(int)response.StatusCode} {ExtractErrorMessage(raw)}");

            string? text = ExtractText(raw, out string problem);
            return text is null ? (response.StatusCode, null, problem) : (response.StatusCode, text, "");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return (HttpStatusCode.RequestTimeout, null, "시간 초과");
        }
        catch (HttpRequestException ex)
        {
            // 네트워크 오류 메시지에는 URL(키 포함)이 들어갈 수 있어 형식만 남긴다.
            return (HttpStatusCode.ServiceUnavailable, null, $"네트워크 오류({ex.GetType().Name})");
        }
    }

    private static bool IsTransient(HttpStatusCode? status)
        => status is HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout or HttpStatusCode.RequestTimeout;

    /// <summary>생각(thought) 파트를 제외한 텍스트 파트만 이어 붙인다.</summary>
    private static string? ExtractText(string raw, out string problem)
    {
        problem = "빈 응답";
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("candidates", out var cands) || cands.GetArrayLength() == 0)
            {
                problem = doc.RootElement.TryGetProperty("promptFeedback", out var fb)
                    ? "응답 후보 없음(안전 필터 가능성): " + Truncate(fb.GetRawText())
                    : "응답 후보 없음";
                return null;
            }

            var sb = new StringBuilder();
            if (cands[0].TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
            {
                foreach (var part in parts.EnumerateArray())
                {
                    bool isThought = part.TryGetProperty("thought", out var t) && t.ValueKind == JsonValueKind.True;
                    if (!isThought && part.TryGetProperty("text", out var text)) sb.Append(text.GetString());
                }
            }

            if (sb.Length == 0)
            {
                string finish = cands[0].TryGetProperty("finishReason", out var fr) ? fr.GetString() ?? "" : "";
                problem = $"텍스트 없음(finishReason={finish})";
                return null;
            }
            return sb.ToString();
        }
        catch (JsonException)
        {
            problem = "응답 JSON 파싱 실패";
            return null;
        }
    }

    private static string ExtractErrorMessage(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.TryGetProperty("message", out var msg))
                return Truncate(msg.GetString() ?? "");
        }
        catch (JsonException) { }
        return Truncate(raw);
    }

    private static string Truncate(string s) => s.Length <= 160 ? s : s[..160] + "…";
}
