using coreservice.Application.Events;
using coreservice.Application.Interfaces;
using coreservice.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace coreservice.Handlers;

public class AiSummarizeHandler(
    IServiceScopeFactory scopeFactory,
    IAiSummarizeService ai,
    IEventPublisher publisher,
    ILogger<AiSummarizeHandler> logger)
    : IEventHandler<CourseSyncedEvent>
{
    public async Task Handle(CourseSyncedEvent @event)
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

        var itemsWithContent = course.Sections
            .SelectMany(s => s.Items)
            .Where(i => !string.IsNullOrWhiteSpace(i.Content))
            .ToList();

        logger.LogInformation(
            "[AiSummarize]   Hittade {Count} items med innehåll att sammanfatta",
            itemsWithContent.Count);

        if (itemsWithContent.Count == 0)
        {
            logger.LogInformation("[AiSummarize] ✓ Inga items att sammanfatta — klar");
            return;
        }

        var summarized = 0;
        foreach (var item in itemsWithContent)
        {
            logger.LogInformation(
                "[AiSummarize]   ({Index}/{Total}) Sammanfattar: '{Title}'",
                summarized + 1, itemsWithContent.Count, item.Title);

            var summary = await ai.SummarizeAsync(item.Content, course.Title);
            item.AiSummary = summary;
            summarized++;

            await publisher.Publish(new ContentSummarizedEvent(
                course.Id,
                item.Id,
                summary,
                DateTime.UtcNow));
        }

        await db.SaveChangesAsync();

        logger.LogInformation(
            "[AiSummarize] ✓ Kurs '{Title}' komplett — {Count}/{Total} sammanfattningar sparade",
            course.Title, summarized, itemsWithContent.Count);
    }
}
