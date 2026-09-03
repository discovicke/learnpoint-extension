using coreservice.Application.Events;
using coreservice.Application.Interfaces;

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
    }
}
