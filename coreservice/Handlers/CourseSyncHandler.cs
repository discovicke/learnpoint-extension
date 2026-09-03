using coreservice.Application.Events;
using coreservice.Application.Interfaces;
using coreservice.Domain.Models;
using coreservice.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace coreservice.Handlers;

public class CourseSyncHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<CourseSyncHandler> logger)
    : IEventHandler<NewContentUploadedEvent>
{
    public async Task Handle(NewContentUploadedEvent @event)
    {
        var course = @event.Course;

        logger.LogInformation(
            "[CourseSync] → Startar synkronisering av kurs '{Title}' (GroupId={GroupId}, {SectionCount} sektioner)",
            course.GroupTitle, course.GroupId, course.Sections.Count);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (tracked, newItems, updatedItems) = await UpsertCourseAsync(db, course);

        logger.LogInformation(
            "[CourseSync] ✓ Kurs '{Title}' (ID={CourseId}) synkad till DB — {NewItems} nya, {UpdatedItems} uppdaterade (ingen AI anropad)",
            tracked.Title, tracked.Id, newItems, updatedItems);
    }

    private async Task<(TrackedCourse course, int newItems, int updatedItems)> UpsertCourseAsync(AppDbContext _db, Course course)
    {
        var groupId = int.Parse(course.GroupId);
        var tracked = await _db.Courses
            .Include(c => c.Sections)
                .ThenInclude(s => s.Items)
            .FirstOrDefaultAsync(c => c.GroupId == groupId);

        var totalNew = 0;
        var totalUpdated = 0;

        if (tracked is null)
        {
            logger.LogDebug("[CourseSync]   Ny kurs hittades inte (GroupId={GroupId}) — skapar ny", groupId);

            tracked = new TrackedCourse
            {
                GroupId = groupId,
                Title = course.GroupTitle,
                SubTitle = course.GroupSubTitle,
                Grade = course.CourseGrade,
                LastScrapedAt = course.ScrapedAt,
                Sections = course.Sections.Select(s => new TrackedSection
                {
                    Title = s.Title,
                    Description = s.Description,
                    Items = s.Items.Select(i => new TrackedItem
                    {
                        ExternalItemId = ExtractItemId(i.Href),
                        Title = i.Title,
                        Status = i.Status,
                        Date = ParseDate(i.Date),
                        ScrapedAt = course.ScrapedAt,
                    }).ToList(),
                }).ToList(),
            };

            totalNew = tracked.Sections.SelectMany(s => s.Items).Count();
            _db.Courses.Add(tracked);
        }
        else
        {
            logger.LogDebug("[CourseSync]   Befintlig kurs hittad (ID={Id}, GroupId={GroupId}) — uppdaterar",
                tracked.Id, groupId);

            tracked.Title = course.GroupTitle;
            tracked.SubTitle = course.GroupSubTitle;
            tracked.Grade = course.CourseGrade;
            tracked.LastScrapedAt = course.ScrapedAt;

            foreach (var section in course.Sections)
            {
                var trackedSection = tracked.Sections
                    .FirstOrDefault(s => s.Title == section.Title);

                if (trackedSection is null)
                {
                    logger.LogDebug("[CourseSync]   Ny sektion: '{Title}'", section.Title);

                    trackedSection = new TrackedSection
                    {
                        Title = section.Title,
                        Description = section.Description,
                        Items = section.Items.Select(i => new TrackedItem
                        {
                            ExternalItemId = ExtractItemId(i.Href),
                            Title = i.Title,
                            Status = i.Status,
                            Date = ParseDate(i.Date),
                            ScrapedAt = course.ScrapedAt,
                        }).ToList(),
                    };
                    totalNew += trackedSection.Items.Count;
                    tracked.Sections.Add(trackedSection);
                }
                else
                {
                    trackedSection.Description = section.Description;

                    foreach (var item in section.Items)
                    {
                        var trackedItem = trackedSection.Items
                            .FirstOrDefault(i => i.ExternalItemId == ExtractItemId(item.Href));

                        if (trackedItem is null)
                        {
                            logger.LogDebug("[CourseSync]   Ny item: '{Title}'", item.Title);

                            trackedSection.Items.Add(new TrackedItem
                            {
                                ExternalItemId = ExtractItemId(item.Href),
                                Title = item.Title,
                                Status = item.Status,
                                Date = ParseDate(item.Date),
                                ScrapedAt = course.ScrapedAt,
                            });
                            totalNew++;
                        }
                        else
                        {
                            trackedItem.Title = item.Title;
                            trackedItem.Status = item.Status;
                            trackedItem.Date = ParseDate(item.Date);
                            trackedItem.ScrapedAt = course.ScrapedAt;
                            totalUpdated++;
                        }
                    }
                }
            }

            foreach (var item in course.Items)
            {
                var existing = tracked.Sections
                    .SelectMany(s => s.Items)
                    .FirstOrDefault(i => i.ExternalItemId == item.ItemId);

                if (existing is not null)
                {
                    existing.Content = item.Content;
                    existing.AiSummary = null;
                    existing.ScrapedAt = item.ScrapedAt;
                    totalUpdated++;
                }
            }
        }

        await _db.SaveChangesAsync();
        return (tracked, totalNew, totalUpdated);
    }

    private static string ExtractItemId(string href)
    {
        var match = System.Text.RegularExpressions.Regex.Match(href, @"ItemId=(\d+)");
        return match.Success
            ? match.Groups[1].Value
            : href;
    }

    private static DateTime? ParseDate(string date)
    {
        if (string.IsNullOrWhiteSpace(date))
            return null;

        return DateTime.TryParse(date, out var result)
            ? result
            : null;
    }
}
