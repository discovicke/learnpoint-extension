using coreservice.Application.Events;
using coreservice.Application.Interfaces;
using coreservice.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace coreservice.Endpoints;

public static class TriggerEndpoints
{
    public static void MapTriggerEndpoints(this WebApplication app)
    {
        app.MapPost("/api/trigger/{groupId:int}", async (
            int groupId,
            IScraperService scraper,
            IEventPublisher events,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Endpoint.Trigger");

            logger.LogInformation(
                "[Trigger] → POST /api/trigger/{GroupId} — Scraping startas",
                groupId);

            var course = await scraper.ScrapeCourseAsync(groupId);

            logger.LogInformation(
                "[Trigger]   Scraping klar: '{Title}' ({SectionCount} sektioner, {ItemCount} items)",
                course.GroupTitle, course.Sections.Count, course.Items.Count);

            await events.Publish(new NewContentUploadedEvent(course, DateTime.UtcNow));

            logger.LogInformation(
                "[Trigger] ✓ Event publicerat — hantering pågår i bakgrunden");

            return Results.Ok(new
            {
                message = "Scraping klar, event publicerat.",
                groupTitle = course.GroupTitle,
                sections = course.Sections.Count,
                items = course.Items.Count,
            });
        });

        app.MapPost("/api/trigger/all", async (
            IScraperService scraper,
            IEventPublisher events,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Endpoint.Trigger");

            var groupIds = scraper.RegisteredGroupIds;
            if (groupIds.Count == 0)
            {
                logger.LogWarning("[Trigger] ⚠ Inga registrerade grupper — kan inte trigga 'all'");
                return Results.BadRequest(new { message = "Inga registrerade grupper i scraper-service/.env" });
            }

            logger.LogInformation(
                "[Trigger] → POST /api/trigger/all — Deep-scraping av {Count} kurser startas",
                groupIds.Count);

            var courses = await scraper.ScrapeCoursesAsync(groupIds);

            logger.LogInformation(
                "[Trigger]   Hämtade {Count} kurser — publicerar event för varje",
                courses.Count);

            var synced = 0;
            foreach (var course in courses)
            {
                logger.LogInformation(
                    "[Trigger]   Publicerar event för '{Title}' (GroupId={GroupId})",
                    course.GroupTitle, course.GroupId);

                await events.Publish(new NewContentUploadedEvent(course, DateTime.UtcNow));
                synced++;
            }

            logger.LogInformation(
                "[Trigger] ✓ {Count}/{Total} kurser vidarebefordrade för synkronisering",
                synced, courses.Count);

            return Results.Ok(new
            {
                message = $"Deep-scraping klar, {synced} kurser skickade till synkronisering.",
                totalRequested = groupIds.Count,
                totalScraped = courses.Count,
                totalSynced = synced,
            });
        });


        app.MapPost("/api/sections/{sectionId:int}/summarize", async (
            int sectionId,
            AppDbContext db,
            IEventPublisher events,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Endpoint.Summarize");

            var section = await db.Sections
                .Include(s => s.Items)
                .FirstOrDefaultAsync(s => s.Id == sectionId);

            if (section is null)
            {
                logger.LogWarning("[Summarize] ⚠ Sektion med ID={SectionId} hittades inte", sectionId);
                return Results.NotFound(new { message = $"Sektion med ID={sectionId} hittades inte." });
            }

            if (!section.Items.Any(i => !string.IsNullOrWhiteSpace(i.Content)))
                return Results.BadRequest(new { message = "Sektionen saknar innehåll att sammanfatta." });

            logger.LogInformation(
                "[Summarize] → POST /api/sections/{SectionId}/summarize — '{Title}' (manuell)",
                sectionId, section.Title);

            section.AiSummary = null;
            section.SummarizedAt = null;
            await db.SaveChangesAsync();

            await events.Publish(new SectionRegisteredEvent(section.TrackedCourseId, section.Id));

            logger.LogInformation("[Summarize] ✓ Event publicerat för '{Title}'", section.Title);
            return Results.Ok(new { sectionId = section.Id, title = section.Title });
        });
    }
}
