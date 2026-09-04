using coreservice.Events;
using coreservice.Infrastructure.Data;
using coreservice.Interfaces;
using coreservice.Models;
using Microsoft.EntityFrameworkCore;

namespace coreservice.Handlers;

public class CourseSyncHandler(
    IServiceScopeFactory scopeFactory,
    IEventPublisher events,
    ILogger<CourseSyncHandler> logger)
    : IEventHandler<NewContentUploadedEvent>
{
    public async Task Handle(NewContentUploadedEvent @event)
    {
        var course = @event.Course;

        logger.LogInformation(
            "[CourseSync] Startar synkronisering av kurs '{Title}' (GroupId={GroupId}, {SectionCount} sektioner)",
            course.GroupTitle, course.GroupId, course.Sections.Count);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (tracked, newItems, updatedItems, sectionsToNotify) = await UpsertCourseAsync(db, course);

        logger.LogInformation(
            "[CourseSync] ✓ Kurs '{Title}' (ID={CourseId}) synkad till DB - {NewItems} nya, {UpdatedItems} uppdaterade",
            tracked.Title, tracked.Id, newItems, updatedItems);
        
        foreach (var sectionId in sectionsToNotify)
        {
            logger.LogInformation(
                "[CourseSync] Publicerar SectionRegisteredEvent (CourseId={CourseId}, SectionId={SectionId})",
                tracked.Id, sectionId);
            await events.Publish(new SectionRegisteredEvent(tracked.Id, sectionId));
        }

        if (sectionsToNotify.Count == 0)
            logger.LogInformation("[CourseSync]   Inga nya veckor att skicka till AI (ingen ny sektion i obetygsatt kurs)");
    }

    private async Task<(TrackedCourse course, int newItems, int updatedItems, List<int> sectionsToNotify)> UpsertCourseAsync(AppDbContext _db, Course course)
    {
        var groupId = int.Parse(course.GroupId);
        var tracked = await _db.Courses
            .Include(c => c.Sections)
                .ThenInclude(s => s.Items)
            .FirstOrDefaultAsync(c => c.GroupId == groupId);

        var totalNew = 0;
        var totalUpdated = 0;
        var newlyAddedSections = new List<TrackedSection>();
        var isNewCourse = tracked is null;

        if (tracked is null)
        {
            logger.LogDebug("[CourseSync]   Ny kurs hittades inte (GroupId={GroupId}) - skapar ny", groupId);

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
            newlyAddedSections.AddRange(tracked.Sections);
            _db.Courses.Add(tracked);
        }
        else
        {
            logger.LogDebug("[CourseSync]   Befintlig kurs hittad (ID={Id}, GroupId={GroupId}) - uppdaterar",
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
                    newlyAddedSections.Add(trackedSection);
                }
                else
                {
                    if (trackedSection.Description != section.Description)
                    {
                        trackedSection.Description = section.Description;
                        trackedSection.AiSummary = null;
                        trackedSection.SummarizedAt = null;
                    }

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

                            // Nytt delmoment i veckan - ogiltigförklara veckosammanfattningen
                            trackedSection.AiSummary = null;
                            trackedSection.SummarizedAt = null;
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
        }
        
        var contentsByItemId = new Dictionary<string, CourseItem>();
        foreach (var scrapedItem in course.Items)
            contentsByItemId.TryAdd(scrapedItem.ItemId, scrapedItem);

        foreach (var section in tracked.Sections)
        {
            foreach (var trackedItem in section.Items)
            {
                if (!contentsByItemId.TryGetValue(trackedItem.ExternalItemId, out var scraped)
                    || string.IsNullOrWhiteSpace(scraped.Content))
                    continue;

                if (trackedItem.Content != scraped.Content)
                {
                    trackedItem.Content = scraped.Content;
                    trackedItem.ScrapedAt = scraped.ScrapedAt;
                    if (!isNewCourse)
                        totalUpdated++;
                    
                    section.AiSummary = null;
                    section.SummarizedAt = null;
                }
                else
                {
                    trackedItem.ScrapedAt = scraped.ScrapedAt;
                }
            }
        }

        await _db.SaveChangesAsync();
        
        var sectionsToNotify = new List<int>();
        if (!HasGrade(tracked.Grade))
        {
            foreach (var section in newlyAddedSections)
            {
                if (section.Items.Any(i => !string.IsNullOrWhiteSpace(i.Content)))
                    sectionsToNotify.Add(section.Id);
                else
                    logger.LogDebug("[CourseSync]   Hoppar över sektion '{Title}' - saknar innehåll att sammanfatta",
                        section.Title);
            }
        }
        else
        {
            logger.LogDebug("[CourseSync]   Kursen har betyg - inga AI-events (kursen är avslutad)");
        }

        return (tracked, totalNew, totalUpdated, sectionsToNotify);
    }

    private static bool HasGrade(string? grade) => !string.IsNullOrWhiteSpace(grade);

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
