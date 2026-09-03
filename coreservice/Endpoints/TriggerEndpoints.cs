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

        app.MapPost("/api/summarize/{courseId:int}", async (
            int courseId,
            AppDbContext db,
            IEventPublisher events,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Endpoint.Summarize");

            var course = await db.Courses
                .Include(c => c.Sections)
                    .ThenInclude(s => s.Items)
                .FirstOrDefaultAsync(c => c.Id == courseId);

            if (course is null)
            {
                logger.LogWarning("[Summarize] ⚠ Kurs med ID={CourseId} hittades inte", courseId);
                return Results.NotFound(new { message = $"Kurs med ID={courseId} hittades inte." });
            }

            var itemsWithContent = course.Sections
                .SelectMany(s => s.Items)
                .Where(i => !string.IsNullOrWhiteSpace(i.Content))
                .ToList();

            var alreadySummarized = itemsWithContent.Count(i => i.AiSummary is not null);

            logger.LogInformation(
                "[Summarize] → POST /api/summarize/{CourseId} — '{Title}': {Total} items med innehåll, {Skipped} redan sammanfattade",
                courseId, course.Title, itemsWithContent.Count, alreadySummarized);

            await events.Publish(new SummarizeCourseEvent(course.Id, course.Title));

            var afterCount = await db.Courses
                .Where(c => c.Id == courseId)
                .SelectMany(c => c.Sections)
                .SelectMany(s => s.Items)
                .CountAsync(i => !string.IsNullOrWhiteSpace(i.Content) && i.AiSummary != null);

            var summarized = afterCount - alreadySummarized;
            var skipped = alreadySummarized;

            logger.LogInformation(
                "[Summarize] ✓ Kurs '{Title}' klar — {Summarized} nya, {Skipped} hoppade över, {Total} med innehåll",
                course.Title, summarized, skipped, itemsWithContent.Count);

            return Results.Ok(new
            {
                courseId = course.Id,
                title = course.Title,
                summarized,
                skipped,
                total = itemsWithContent.Count,
            });
        });
    }
}
