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
            IEventPublisher events) =>
        {
            var course = await scraper.ScrapeCourseAsync(groupId);

            await events.Publish(new NewContentUploadedEvent(course, DateTime.UtcNow));

            return Results.Ok(new
            {
                message = "Scraping klar, event publicerat.",
                groupTitle = course.GroupTitle,
                sections = course.Sections.Count,
                items = course.Items.Count,
            });
        });
    }
}
