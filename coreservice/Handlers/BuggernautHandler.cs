using coreservice.Events;
using coreservice.Interfaces;

namespace coreservice.Handlers;

public class BuggernautHandler(
    IBuggernautService buggernaut,
    IConfiguration config,
    ILogger<BuggernautHandler> logger)
    : IEventHandler<WeekSummarizedEvent>
{
    private static readonly string[] Difficulties = ["Easy", "Medium", "Hard"];

    public async Task Handle(WeekSummarizedEvent @event)
    {
        try
        {
            var category = config.GetValue<string>("Buggernaut:Category") ?? "General";
            var dryRun = config.GetValue("Buggernaut:DryRun", false);

            logger.LogInformation("[Buggernaut] → Genererar {Count} uppgifter för '{Title}' (kategori={Category}, Easy→Hard)",
                Difficulties.Length, @event.Title, category);

            var succeeded = 0;
            var failed = 0;
            foreach (var difficulty in Difficulties)
            {
                try
                {
                    var topic = $"{@event.Title}\n\n{@event.AiSummary}";
                    var result = await buggernaut.GenerateAsync(topic, category, difficulty, dryRun);
                    if (result.Success)
                        succeeded++;
                    else
                    {
                        failed++;
                        logger.LogWarning("[Buggernaut]   ✗ {Difficulty} misslyckades — fortsätter med nästa", difficulty);
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    logger.LogError(ex, "[Buggernaut]   ✗ {Difficulty} kastade fel — fortsätter med nästa", difficulty);
                }
            }

            logger.LogInformation("[Buggernaut] ✓ '{Title}' klar — {Succeeded} lyckade, {Failed} misslyckade",
                @event.Title, succeeded, failed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Buggernaut] ✗ Oväntat fel vid uppgiftsgenerering för '{Title}'", @event.Title);
        }
    }
}
