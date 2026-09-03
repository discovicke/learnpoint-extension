using System.Net.Http.Json;
using coreservice.Application.Events;
using coreservice.Application.Interfaces;

namespace coreservice.Handlers;

public class BuggernautHandler(
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ILogger<BuggernautHandler> logger)
    : IEventHandler<WeekSummarizedEvent>
{
    public async Task Handle(WeekSummarizedEvent @event)
    {
        try
        {
            var baseUrl = config["Buggernaut:BaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                logger.LogWarning("[Buggernaut] ⚠ Buggernaut:BaseUrl saknas i konfigurationen — hoppar över uppgiftsgenerering för '{Title}'",
                    @event.Title);
                return;
            }

            var difficulty = config.GetValue<string>("Buggernaut:Difficulty") ?? "normal";
            var url = $"{baseUrl.TrimEnd('/')}/api/buggernaut/generate";

            logger.LogInformation("[Buggernaut] → Genererar 3 uppgifter för '{Title}' (difficulty={Difficulty})",
                @event.Title, difficulty);

            var client = httpFactory.CreateClient();
            using var response = await client.PostAsJsonAsync(url, new
            {
                topic = @event.AiSummary,
                count = 3,
                difficulty,
            });

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Buggernaut-wrappern svarade {(int)response.StatusCode}: {body}");
            }

            logger.LogInformation("[Buggernaut] ✓ 3 uppgifter skapade för '{Title}'", @event.Title);
        }
        catch (Exception ex)
        {
            // Får aldrig kasta — SMS-flödet ska inte påverkas.
            logger.LogError(ex, "[Buggernaut] ✗ Misslyckades med uppgiftsgenerering för '{Title}'", @event.Title);
        }
    }
}
