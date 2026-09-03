using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using coreservice.Application.Interfaces;

namespace coreservice.Infrastructure.Ai;

public class GeminiSummarizeAdapter : IAiSummarizeService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly ILogger<GeminiSummarizeAdapter> _logger;

    public GeminiSummarizeAdapter(HttpClient http, IConfiguration config, ILogger<GeminiSummarizeAdapter> logger)
    {
        _http = http;
        _logger = logger;
        _apiKey = config["GoogleAi:ApiKey"]
            ?? throw new InvalidOperationException("GoogleAi:ApiKey saknas i konfigurationen. Kör: dotnet user-secrets set \"GoogleAi:ApiKey\" \"din-nyckel\"");
        _model = config.GetValue<string>("GoogleAi:Model") ?? "gemini-3.5-flash";
    }

    public async Task<string> SummarizeSectionAsync(
        string sectionTitle,
        string sectionDescription,
        IReadOnlyList<(string Title, string Content)> items,
        string courseTitle)
    {
        var sb = new StringBuilder();
        foreach (var (title, content) in items)
        {
            sb.AppendLine($"### {title}");
            sb.AppendLine(content);
            sb.AppendLine();
        }

        var prompt = $"""
            Du är en studiehandledare. Sammanfatta följande vecka/moment i en kurs för en junior fullstackstudent.
            Fokusera på veckans tema, nyckelkoncept och viktiga begrepp. Det ska läsas som en förstudie innan föreläsning om samma område.
            Svaret ska vara på svenska och max 160 ord.

            Kurs: {courseTitle}
            Vecka: {sectionTitle}
            Beskrivning: {sectionDescription}

            Delmoment:
            {sb}
            """;

        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            },
            generationConfig = new
            {
                temperature = 0.3,
                maxOutputTokens = 1024,
            }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

        var response = await _http.PostAsJsonAsync(url, requestBody);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            _logger.LogError(
                "[Gemini] ✗ API-anrop misslyckades: modell='{Model}' status={StatusCode}\n{ErrorBody}",
                _model, (int)response.StatusCode, errorBody);
            throw new InvalidOperationException(
                $"Gemini API misslyckades (modell='{_model}', status={(int)response.StatusCode} {response.StatusCode}). " +
                $"Kontrollera att modellen är tillgänglig för din nyckel via GET /v1beta/models. Svar: {errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<GeminiResponse>();

        return result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text
            ?? "Ingen sammanfattning genererad.";
    }
}

internal class GeminiResponse
{
    [JsonPropertyName("candidates")]
    public List<GeminiCandidate>? Candidates { get; set; }
}

internal class GeminiCandidate
{
    [JsonPropertyName("content")]
    public GeminiContent? Content { get; set; }
}

internal class GeminiContent
{
    [JsonPropertyName("parts")]
    public List<GeminiPart>? Parts { get; set; }
}

internal class GeminiPart
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}
