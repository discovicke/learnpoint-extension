using coreservice.Application.Events;
using coreservice.Application.Interfaces;
using coreservice.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace coreservice.Handlers;

public class SummaryFileHandler(
    IServiceScopeFactory scopeFactory,
    ISectionSummaryStore store,
    ILogger<SummaryFileHandler> logger)
    : IEventHandler<WeekSummarizedEvent>
{
    public async Task Handle(WeekSummarizedEvent @event)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var course = await db.Courses
                .Include(c => c.Sections)
                    .ThenInclude(s => s.Items)
                .FirstOrDefaultAsync(c => c.Id == @event.CourseId);

            var section = course?.Sections.FirstOrDefault(s => s.Id == @event.SectionId);
            if (section is null)
            {
                logger.LogWarning("[SummaryFile] ⚠ Sektion {SectionId} hittades inte — ingen fil skrivs", @event.SectionId);
                return;
            }

            var relativePath = await store.SaveAsync(
                course!.Title,
                section.Title,
                section.Description,
                @event.AiSummary,
                section.SummarizedAt ?? DateTime.UtcNow,
                section.Items.Select(i => i.Title).ToList());

            logger.LogInformation("[SummaryFile] ✓ '{Title}' → {RelativePath}", @event.Title, relativePath);
        }
        catch (Exception ex)
        {
            // Får aldrig kasta — SMS/Buggernaut ska köras oavsett.
            logger.LogError(ex, "[SummaryFile] ✗ Kunde inte spara fil för '{Title}'", @event.Title);
        }
    }
}
