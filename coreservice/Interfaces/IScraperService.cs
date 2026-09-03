using coreservice.Domain.Models;

namespace coreservice.Application.Interfaces;

public interface IScraperService
{
    Task<Course> ScrapeCourseAsync(int groupId);

    Task<List<Course>> ScrapeCoursesAsync(IEnumerable<int> groupIds);

    IReadOnlyList<int> RegisteredGroupIds { get; }
}
