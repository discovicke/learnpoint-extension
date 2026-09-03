using coreservice.Application.Events;
using coreservice.Application.Interfaces;
using coreservice.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace coreservice.Handlers;

public class AiSummarizeHandler(
    IServiceScopeFactory scopeFactory,
    IAiSummarizeService ai,
    IEventPublisher events,
    ILogger<AiSummarizeHandler> logger)
    : IEventHandler<SectionRegisteredEvent>
{
    public async Task Handle(SectionRegisteredEvent @event)
    {
        logger.LogInformation(
            "[AiSummarize] → SectionRegisteredEvent mottaget (CourseId={CourseId}, SectionId={SectionId})",
            @event.CourseId, @event.SectionId);

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

        var section = course.Sections.FirstOrDefault(s => s.Id == @event.SectionId);
        if (section is null)
        {
            logger.LogWarning("[AiSummarize] ⚠ Sektion med ID={SectionId} hittades inte i kurs {CourseId} — avbryter",
                @event.SectionId, @event.CourseId);
            return;
        }

        if (section.AiSummary is not null)
        {
            logger.LogInformation("[AiSummarize]   Vecka '{Title}' redan sammanfattad — hoppar över", section.Title);
            return;
        }

        var itemsWithContent = section.Items
            .Where(i => !string.IsNullOrWhiteSpace(i.Content))
            .Select(i => (i.Title, i.Content))
            .ToList();

        if (itemsWithContent.Count == 0)
        {
            logger.LogInformation("[AiSummarize]   Vecka '{Title}' saknar innehåll — inget att sammanfatta", section.Title);
            return;
        }

        logger.LogInformation(
            "[AiSummarize]   Sammanfattar vecka: '{Title}' ({ItemCount} delmoment med innehåll)",
            section.Title, itemsWithContent.Count);

        try
        {
            section.AiSummary = await ai.SummarizeSectionAsync(
                section.Title, section.Description, itemsWithContent, course.Title);
            section.SummarizedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AiSummarize]   ✗ Misslyckades med vecka '{Title}'", section.Title);
            return;
        }

        logger.LogInformation("[AiSummarize] ✓ Vecka '{Title}' sammanfattad — publicerar WeekSummarizedEvent", section.Title);

        await events.Publish(new WeekSummarizedEvent(course.Id, section.Id, section.Title, section.AiSummary));
    }
}
