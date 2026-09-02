namespace coreservice.Domain.Models;

public class TrackedCourse
{
    public int Id { get; set; }
    public string GroupId { get; set; } = "";
    public DateTime LastScrapedAt { get; set; }
    public string Title { get; set; } = "";
    public string SubTitle { get; set; } = "";
    public string Grade { get; set; } = "";
    public List<TrackedSection> Sections { get; set; } = [];
}



