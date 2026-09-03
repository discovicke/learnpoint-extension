using coreservice.Application.Interfaces;
using coreservice.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace coreservice.Endpoints;

public static class CourseEndpoints
{
    public static void MapCourseEndpoints(this WebApplication app)
    {
        app.MapGet("/api/courses", async (AppDbContext db, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Endpoint.Courses");

            logger.LogInformation("[Courses] GET /api/courses — Hämtar alla kurser");

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

            logger.LogInformation("[Courses] ✓ Returnerar {Count} kurser", courses.Count);
            return Results.Ok(courses);
        });

        app.MapGet("/api/courses/{id:int}", async (int id, AppDbContext db, ISectionSummaryStore summaries, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Endpoint.Courses");

            logger.LogInformation("[Courses] GET /api/courses/{Id} — Hämtar kursdetalj", id);

            var course = await db.Courses
                .Include(c => c.Sections)
                    .ThenInclude(s => s.Items)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (course is null)
            {
                logger.LogWarning("[Courses] ⚠ Kurs med ID={Id} hittades inte", id);
                return Results.NotFound();
            }

            logger.LogInformation("[Courses] ✓ Returnerar kurs '{Title}' med {SectionCount} sektioner",
                course.Title, course.Sections.Count);

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
                    HasAiSummary = s.AiSummary is not null,
                    s.AiSummary,
                    s.SummarizedAt,
                    SummaryFile = s.AiSummary is not null && summaries.Exists(course.Title, s.Title)
                        ? summaries.GetRelativePath(course.Title, s.Title)
                        : null,
                    Items = s.Items.Select(i => new
                    {
                        i.Id,
                        i.ExternalItemId,
                        i.Title,
                        i.Status,
                        i.Date,
                        i.ScrapedAt,
                    }),
                }),
            });
        });

        app.MapGet("/api/courses/{id:int}/current-week", async (int id, AppDbContext db, ISectionSummaryStore summaries, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Endpoint.Courses");

            logger.LogInformation("[Courses] GET /api/courses/{Id}/current-week", id);

            var course = await db.Courses
                .Include(c => c.Sections)
                    .ThenInclude(s => s.Items)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (course is null)
            {
                logger.LogWarning("[Courses] ⚠ Kurs med ID={Id} hittades inte", id);
                return Results.NotFound();
            }

            var latestSection = course.Sections
                .OrderByDescending(s => s.Items.Max(i => i.ScrapedAt))
                .FirstOrDefault();

            if (latestSection is null)
            {
                logger.LogWarning("[Courses] ⚠ Ingen sektion för kurs {Id}", id);
                return Results.NotFound(new { message = "Inga sektioner" });
            }

            logger.LogInformation("[Courses] ✓ Returnerar senaste vecka: '{Title}'", latestSection.Title);

            return Results.Ok(new
            {
                Section = latestSection.Title,
                Description = latestSection.Description,
                HasAiSummary = latestSection.AiSummary is not null,
                AiSummary = latestSection.AiSummary,
                SummarizedAt = latestSection.SummarizedAt,
                SummaryFile = latestSection.AiSummary is not null && summaries.Exists(course.Title, latestSection.Title)
                    ? summaries.GetRelativePath(course.Title, latestSection.Title)
                    : null,
                Items = latestSection.Items.Select(i => new
                {
                    i.Id,
                    i.Title,
                    i.Status,
                    i.Date,
                }),
            });
        });

        app.MapGet("/api/courses/{id:int}/incomplete", async (int id, AppDbContext db, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Endpoint.Courses");

            logger.LogInformation("[Courses] GET /api/courses/{Id}/incomplete", id);

            var course = await db.Courses
                .Include(c => c.Sections)
                    .ThenInclude(s => s.Items)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (course is null)
            {
                logger.LogWarning("[Courses] ⚠ Kurs med ID={Id} hittades inte", id);
                return Results.NotFound();
            }

            var incomplete = course.Sections
                .SelectMany(s => s.Items.Where(i => i.Status != "Klar"))
                .Select(i => new
                {
                    i.Id,
                    i.Title,
                    i.Status,
                    i.Date,
                    Section = course.Sections.First(s => s.Id == i.TrackedSectionId).Title,
                })
                .ToList();

            logger.LogInformation("[Courses] ✓ Returnerar {Count} ej klara items", incomplete.Count);
            return Results.Ok(incomplete);
        });

        app.MapGet("/api/courses/{id:int}/completed", async (int id, AppDbContext db, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Endpoint.Courses");

            logger.LogInformation("[Courses] GET /api/courses/{Id}/completed", id);

            var course = await db.Courses
                .Include(c => c.Sections)
                    .ThenInclude(s => s.Items)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (course is null)
            {
                logger.LogWarning("[Courses] ⚠ Kurs med ID={Id} hittades inte", id);
                return Results.NotFound();
            }

            var completed = course.Sections
                .SelectMany(s => s.Items.Where(i => i.Status == "Klar"))
                .Select(i => new
                {
                    i.Id,
                    i.Title,
                    i.Status,
                    i.Date,
                    Section = course.Sections.First(s => s.Id == i.TrackedSectionId).Title,
                })
                .ToList();

            logger.LogInformation("[Courses] ✓ Returnerar {Count} klara items", completed.Count);
            return Results.Ok(completed);
        });
    }
}
