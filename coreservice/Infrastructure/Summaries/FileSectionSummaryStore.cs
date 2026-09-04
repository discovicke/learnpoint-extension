using System.Text;
using coreservice.Interfaces;

namespace coreservice.Infrastructure.Summaries;

public class FileSectionSummaryStore(
    IConfiguration config,
    IWebHostEnvironment env,
    ILogger<FileSectionSummaryStore> logger) : ISectionSummaryStore
{
    private string BaseDirectory =>
        config.GetValue<string>("Summaries:Directory") is { Length: > 0 } configured
            ? configured
            : Path.Combine(env.ContentRootPath, "summaries");

    public string GetRelativePath(string courseTitle, string sectionTitle) =>
        $"summaries/{ToSafeFileName(courseTitle)}/{ToSafeFileName(sectionTitle)}.md";

    public bool Exists(string courseTitle, string sectionTitle) =>
        File.Exists(Path.Combine(BaseDirectory, ToSafeFileName(courseTitle), $"{ToSafeFileName(sectionTitle)}.md"));

    public async Task<string> SaveAsync(
        string courseTitle,
        string sectionTitle,
        string sectionDescription,
        string summary,
        DateTime summarizedAt,
        IReadOnlyList<string> itemTitles)
    {
        var dir = Path.Combine(BaseDirectory, ToSafeFileName(courseTitle));
        Directory.CreateDirectory(dir);

        var fileName = $"{ToSafeFileName(sectionTitle)}.md";
        var fullPath = Path.Combine(dir, fileName);

        var sb = new StringBuilder();
        sb.AppendLine($"# {sectionTitle.Trim()}");
        sb.AppendLine();
        sb.AppendLine($"Kurs: {courseTitle.Trim()}");
        sb.AppendLine($"Sammanfattad (UTC): {summarizedAt:yyyy-MM-dd HH:mm}");
        if (!string.IsNullOrWhiteSpace(sectionDescription))
        {
            sb.AppendLine();
            sb.AppendLine(sectionDescription.Trim());
        }
        sb.AppendLine();
        sb.AppendLine("## Sammanfattning");
        sb.AppendLine();
        sb.AppendLine(summary.Trim());
        sb.AppendLine();
        sb.AppendLine("## Delmoment");
        sb.AppendLine();
        foreach (var itemTitle in itemTitles)
            sb.AppendLine($"- {itemTitle.Trim()}");

        await File.WriteAllTextAsync(fullPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        logger.LogInformation("[SummaryFile] ✓ Sparad: {Path}", fullPath);

        return GetRelativePath(courseTitle, sectionTitle);
    }

    internal static string ToSafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name.Trim())
            sb.Append(invalid.Contains(ch) ? '-' : ch);

        // Windows gillar inte filer som slutar med punkt/mellanslag.
        var safe = sb.ToString().Trim().TrimEnd('.', ' ');
        if (safe.Length > 80)
            safe = safe[..80].TrimEnd('.', ' ');

        return safe.Length == 0 ? "vecka" : safe;
    }
}
