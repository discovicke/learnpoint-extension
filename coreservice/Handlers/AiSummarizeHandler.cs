using coreservice.Application.Events;
using coreservice.Application.Interfaces;
using coreservice.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace coreservice.Handlers;

public class AiSummarizeHandler(
    IServiceScopeFactory scopeFactory,
    IAiSummarizeService ai,
    ILogger<AiSummarizeHandler> logger)
    : IEventHandler<SummarizeCourseEvent>
{
    public async Task Handle(SummarizeCourseEvent @event)
    {
        logger.LogInformation(
            "[AiSummarize] → Startar AI-sammanfattning för kurs '{Title}' (ID={CourseId})",
            @event.Title, @event.CourseId);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var course = await db.Courses
            .Include(c => c.Sections)
                .ThenInclude(s => s.Items)
            .FirstOrDefaultAsync(c => c.Id == @event.CourseId);

        if (course is null)
        {
            logger.LogWarning("[AiSummarize] ⚠ Kurs med ID={CourseId} hittades inte i DB — avbryter", @event.CourseId);
            return;
        }

        var allItems = course.Sections.SelectMany(s => s.Items).ToList();

        var toSummarize = allItems
            .Where(i => !string.IsNullOrWhiteSpace(i.Content) && i.AiSummary is null)
            .ToList();

        var skipped = allItems.Count(i => !string.IsNullOrWhiteSpace(i.Content) && i.AiSummary is not null);

        logger.LogInformation(
            "[AiSummarize]   {ToSummarize} items att sammanfatta, {Skipped} redan sammanfattade (hoppas över)",
            toSummarize.Count, skipped);

        if (toSummarize.Count == 0)
        {
            logger.LogInformation("[AiSummarize] ✓ Inget nytt att sammanfatta — klar");
            return;
        }

        var summarized = 0;
        var failed = 0;
        foreach (var item in toSummarize)
        {
            logger.LogInformation(
                "[AiSummarize]   ({Index}/{Total}) Sammanfattar: '{Title}'",
                summarized + failed + 1, toSummarize.Count, item.Title);

            try
            {
                item.AiSummary = await ai.SummarizeAsync(item.Content, course.Title);
                summarized++;
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogError(ex,
                    "[AiSummarize]   ✗ Misslyckades med '{Title}' — fortsätter med nästa",
                    item.Title);
            }
        }

        await db.SaveChangesAsync();

        logger.LogInformation(
            "[AiSummarize] ✓ Kurs '{Title}' klar — {Summarized} sammanfattade, {Skipped} hoppade över, {Failed} misslyckade",
            course.Title, summarized, skipped, failed);
    }
}
