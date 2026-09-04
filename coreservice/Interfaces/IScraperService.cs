using coreservice.Models;

namespace coreservice.Interfaces;

public interface IScraperService
{
    Task<Course> ScrapeCourseAsync(int groupId);

    Task<List<Course>> ScrapeCoursesAsync(IEnumerable<int> groupIds);

    IReadOnlyList<int> RegisteredGroupIds { get; }
}
