namespace coreservice.Domain.Models;

public class TrackedSection
{
    public int Id { get; set; }
    public int TrackedCourseId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public List<TrackedItem> Items { get; set; } = [];
}