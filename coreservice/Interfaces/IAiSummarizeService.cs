namespace coreservice.Interfaces;

public interface IAiSummarizeService
{
    Task<string> SummarizeSectionAsync(
        string sectionTitle,
        string sectionDescription,
        IReadOnlyList<(string Title, string Content)> items,
        string courseTitle);
}