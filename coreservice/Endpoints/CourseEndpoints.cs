using coreservice.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace coreservice.Endpoints;

public static class CourseEndpoints
{
    public static void MapCourseEndpoints(this WebApplication app)
    {
        app.MapGet("/api/courses", async (AppDbContext db) =>
        {
            var courses = await db.Courses
                .Include(c => c.Sections)
                    .ThenInclude(s => s.Items)
                .OrderByDescending(c => c.LastScrapedAt)
                .Select(c => new
                {
                    c.Id,
                    c.GroupId,
                    c.Title,
                    c.SubTitle,
                    c.Grade,
                    c.LastScrapedAt,
                    TotalItems = c.Sections.Sum(s => s.Items.Count),
                    CompletedItems = c.Sections.Sum(s => s.Items.Count(i => i.Status == "Klar")),
                    CompletionPercent = c.Sections.Sum(s => s.Items.Count) > 0
                        ? Math.Round((double)c.Sections.Sum(s => s.Items.Count(i => i.Status == "Klar"))
                            / c.Sections.Sum(s => s.Items.Count) * 100, 1)
                        : 0,
                    SectionCount = c.Sections.Count,
                })
                .ToListAsync();

            return Results.Ok(courses);
        });

        app.MapGet("/api/courses/{id:int}", async (int id, AppDbContext db) =>
        {
            var course = await db.Courses
                .Include(c => c.Sections)
                    .ThenInclude(s => s.Items)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (course is null) 
                return Results.NotFound();

            return Results.Ok(new
            {
                course.Id,
                course.GroupId,
                course.Title,
                course.SubTitle,
                course.Grade,
                course.LastScrapedAt,
                Sections = course.Sections.Select(s => new
                {
                    s.Id,
                    s.Title,
                    s.Description,
                    Items = s.Items.Select(i => new
                    {
                        i.Id,
                        i.ExternalItemId,
                        i.Title,
                        i.Status,
                        i.Date,
                        i.ScrapedAt,
                        HasAiSummary = i.AiSummary is not null,
                        i.AiSummary,
                    }),
                }),
            });
        });

        app.MapGet("/api/courses/{id:int}/current-week", async (int id, AppDbContext db) =>
        {
            var course = await db.Courses
                .Include(c => c.Sections)
                    .ThenInclude(s => s.Items)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (course is null) 
                return Results.NotFound();

            var latestSection = course.Sections
                .OrderByDescending(s => s.Items.Max(i => i.ScrapedAt))
                .FirstOrDefault();

            if (latestSection is null) 
                return Results.NotFound(new { message = "Inga sektioner" });

            return Results.Ok(new
            {
                Section = latestSection.Title,
                Description = latestSection.Description,
                Items = latestSection.Items.Select(i => new
                {
                    i.Id,
                    i.Title,
                    i.Status,
                    i.Date,
                    HasAiSummary = i.AiSummary is not null,
                    i.AiSummary,
                }),
            });
        });

        app.MapGet("/api/courses/{id:int}/incomplete", async (int id, AppDbContext db) =>
        {
            var course = await db.Courses
                .Include(c => c.Sections)
                    .ThenInclude(s => s.Items)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (course is null) 
                return Results.NotFound();

            var incomplete = course.Sections
                .SelectMany(s => s.Items.Where(i => i.Status != "Klar"))
                .Select(i => new
                {
                    i.Id,
                    i.Title,
                    i.Status,
                    i.Date,
                    Section = course.Sections.First(s => s.Id == i.TrackedSectionId).Title,
                    HasAiSummary = i.AiSummary is not null,
                    i.AiSummary,
                })
                .ToList();

            return Results.Ok(incomplete);
        });

        app.MapGet("/api/courses/{id:int}/completed", async (int id, AppDbContext db) =>
        {
            var course = await db.Courses
                .Include(c => c.Sections)
                    .ThenInclude(s => s.Items)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (course is null) 
                return Results.NotFound();

            var completed = course.Sections
                .SelectMany(s => s.Items.Where(i => i.Status == "Klar"))
                .Select(i => new
                {
                    i.Id,
                    i.Title,
                    i.Status,
                    i.Date,
                    Section = course.Sections.First(s => s.Id == i.TrackedSectionId).Title,
                    HasAiSummary = i.AiSummary is not null,
                    i.AiSummary,
                })
                .ToList();

            return Results.Ok(completed);
        });
    }
}
