namespace coreservice.Domain.Models;

public class Course
{
    public string GroupId { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTime ScrapedAt { get; set; }
    public string GroupTitle { get; set; } = "";
    public string GroupSubTitle { get; set; } = "";
    public string CourseGrade { get; set; } = "";
    public bool NoContent { get; set; }
    public List<Section> Sections { get; set; } = [];
    public List<CourseItem> Items { get; set; } = [];
}
