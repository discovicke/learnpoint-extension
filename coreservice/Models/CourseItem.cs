namespace coreservice.Domain.Models;

public class CourseItem
{
    public string GroupId { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTime ScrapedAt { get; set; }
    public string Title { get; set; } = "";
    public string Meta { get; set; } = "";
    public string Tab { get; set; } = "";
    public string Status { get; set; } = "";
    public string Content { get; set; } = "";
    public List<ItemLink> Links { get; set; } = [];
    public bool HasContent { get; set; }
}
