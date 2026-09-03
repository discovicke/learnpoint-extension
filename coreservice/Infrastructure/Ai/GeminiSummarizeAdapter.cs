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

    public GeminiSummarizeAdapter(HttpClient http, IConfiguration config)
    {
        _http = http;
        _apiKey = config["GoogleAi:ApiKey"]
            ?? throw new InvalidOperationException("GoogleAi:ApiKey saknas i konfigurationen. Kör: dotnet user-secrets set \"GoogleAi:ApiKey\" \"din-nyckel\"");
        _model = config.GetValue<string>("GoogleAi:Model") ?? "gemini-2.0-flash";
    }

    public async Task<string> SummarizeAsync(string content, string courseTitle)
    {
        var prompt = $"""
            Du är en studiehandledare. Sammanfatta följande kursinnehåll för en junior fullstackstudent.
            Fokusera på nyckelkoncept och viktiga begrepp. Det ska läsas som en förstudie innan föreläsning om samma område.
            Svaret ska vara på svenska och max 300 ord.

            Kurs: {courseTitle}

            Innehåll:
            {content}
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
        response.EnsureSuccessStatusCode();

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
