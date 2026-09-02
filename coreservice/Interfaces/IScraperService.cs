using coreservice.Domain.Models;

namespace coreservice.Application.Interfaces;

public interface IScraperService
{
    Task<Course> ScrapeCourseAsync(int groupId);
}