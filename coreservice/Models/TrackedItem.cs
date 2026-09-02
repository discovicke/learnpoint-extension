namespace coreservice.Domain.Models;

public class TrackedItem
{
    public int Id { get; set; }
    public int TrackedSectionId { get; set; }
    public string ExternalItemId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime? Date { get; set; }
    public string Content { get; set; } = "";
    public string? AiSummary { get; set; }
    public DateTime? ScrapedAt { get; set; }
}