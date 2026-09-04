namespace coreservice.Models;

public class Section
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public List<SectionItem> Items { get; set; } = [];
}
