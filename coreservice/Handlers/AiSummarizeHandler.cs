using coreservice.Application.Events;
using coreservice.Application.Interfaces;
using coreservice.Domain.Models;
using coreservice.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace coreservice.Handlers;

public class AiSummarizeHandler(
    IServiceScopeFactory scopeFactory,
    IAiSummarizeService ai,
    IEventPublisher publisher,
    ILogger<AiSummarizeHandler> logger)
    : IEventHandler<NewContentUploadedEvent>
{
    public async Task Handle(NewContentUploadedEvent @event)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var course = @event.Course;

        logger.LogInformation("Bearbetar kurs: {Title} (GroupId={GroupId})", course.GroupTitle, course.GroupId);

        var tracked = await UpsertCourseAsync(db, course);

        var itemsWithContent = tracked.Sections
            .SelectMany(s => s.Items)
            .Where(i => !string.IsNullOrWhiteSpace(i.Content))
            .ToList();

        logger.LogInformation("{Count} items med innehåll att sammanfatta", itemsWithContent.Count);

        foreach (var item in itemsWithContent)
        {
            logger.LogInformation("Sammanfattar: {Title}", item.Title);

            var summary = await ai.SummarizeAsync(item.Content, course.GroupTitle);
            item.AiSummary = summary;

            await publisher.Publish(new ContentSummarizedEvent(
                tracked.Id,
                item.Id,
                summary,
                DateTime.UtcNow));
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Kurs {Title} komplett — {Count} sammanfattningar sparade",
            course.GroupTitle, itemsWithContent.Count);
    }

    private static async Task<TrackedCourse> UpsertCourseAsync(AppDbContext _db, Course course)
    {
        var groupId = int.Parse(course.GroupId);
        var tracked = await _db.Courses
            .Include(c => c.Sections)
                .ThenInclude(s => s.Items)
            .FirstOrDefaultAsync(c => c.GroupId == groupId);

        if (tracked is null)
        {
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

            _db.Courses.Add(tracked);
        }
        else
        {
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
                            trackedSection.Items.Add(new TrackedItem
                            {
                                ExternalItemId = ExtractItemId(item.Href),
                                Title = item.Title,
                                Status = item.Status,
                                Date = ParseDate(item.Date),
                                ScrapedAt = course.ScrapedAt,
                            });
                        }
                        else
                        {
                            trackedItem.Title = item.Title;
                            trackedItem.Status = item.Status;
                            trackedItem.Date = ParseDate(item.Date);
                            trackedItem.ScrapedAt = course.ScrapedAt;
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
                }
            }
        }

        await _db.SaveChangesAsync();
        return tracked;
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
