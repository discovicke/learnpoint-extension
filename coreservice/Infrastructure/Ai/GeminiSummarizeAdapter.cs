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
            Du är en studiehandledare som skriver förstudiematerial åt en junior fullstackstudent.
            Nedan får du en veckas innehåll från en kursportal. Mycket av texten är
            uppgiftsinstruktioner och administration — ditt jobb är att destillera fram
            KUNSKAPEN: vad ska studenten förstå EFTER veckan?

            Regler:
            - Definiera varje nyckelkoncept: vad det ÄR och varför det är viktigt. Skriv aldrig
              bara att ett moment "genomfördes" — förklara begreppen momenten handlar om.
            - Koppla allt till veckans tema. Ignorera ren administration (datum, salar,
              utbildningsdagar, inlämningsformaliteter, gruppindelningar).
            - Svaret ska vara på svenska, max 350 ord, i markdown med exakt dessa rubriker:

            ## Veckans tema
            2–3 meningar om vad veckan handlar om och varför det är viktigt.

            ## Nyckelkoncept
            Punktlista. Varje punkt definierar ETT begrepp (fetstil) + 1–2 meningar förklaring.

            ## Att kunna efter veckan
            Kort punktlista över förmågor/begrepp studenten förväntas behärska.

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
                maxOutputTokens = 2048,
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

        var candidate = result?.Candidates?.FirstOrDefault();
        if (candidate?.FinishReason is { Length: > 0 } finishReason && finishReason != "STOP")
            _logger.LogWarning("[Gemini] ⚠ Genereringen avslutades med finishReason='{FinishReason}' — svaret kan vara kapat",
                finishReason);

        // Konkatenera ALLA parts — att bara ta första kapar svaret mitt i meningen.
        var text = candidate?.Content?.Parts is { Count: > 0 } parts
            ? string.Concat(parts.Select(p => p.Text ?? ""))
            : null;

        return string.IsNullOrWhiteSpace(text)
            ? "Ingen sammanfattning genererad."
            : text;
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

    [JsonPropertyName("finishReason")]
    public string? FinishReason { get; set; }
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
