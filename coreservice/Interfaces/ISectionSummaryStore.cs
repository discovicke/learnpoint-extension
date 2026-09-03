namespace coreservice.Application.Interfaces;

public interface ISectionSummaryStore
{
    string GetRelativePath(string courseTitle, string sectionTitle);
    bool Exists(string courseTitle, string sectionTitle);

    Task<string> SaveAsync(
        string courseTitle,
        string sectionTitle,
        string sectionDescription,
        string summary,
        DateTime summarizedAt,
        IReadOnlyList<string> itemTitles);
}
