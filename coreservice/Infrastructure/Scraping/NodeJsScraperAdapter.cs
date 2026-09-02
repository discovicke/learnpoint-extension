using System.Diagnostics;
using System.Text.Json;
using coreservice.Application.Interfaces;
using coreservice.Domain.Models;

namespace coreservice.Infrastructure.Scraping;

public class NodeJsScraperAdapter : IScraperService
{
    private readonly string _scraperDir;
    private readonly string _nodeCommand;

    public NodeJsScraperAdapter(IConfiguration config)
    {
        _scraperDir = config.GetValue<string>("Scraper:WorkingDirectory")
            ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scraper-service");
        _nodeCommand = config.GetValue<string>("Scraper:NodeCommand") ?? "node";
    }

    public async Task<Course> ScrapeCourseAsync(int groupId)
    {
        var outFile = Path.Combine(Path.GetTempPath(), $"scraper-{groupId}-{Guid.NewGuid()}.json");

        var psi = new ProcessStartInfo
        {
            FileName = _nodeCommand,
            Arguments = "index.js",
            WorkingDirectory = _scraperDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        psi.EnvironmentVariables["LEARNPOINT_GROUP_IDS"] = groupId.ToString();
        psi.EnvironmentVariables["OUT_FILE"] = outFile;
        psi.EnvironmentVariables["DEEP_SCRAPE"] = "true";

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Kunde inte starta Node.js-processen");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Scraper misslyckades (exit {process.ExitCode}):\n{stderr}");

        if (!File.Exists(outFile))
            throw new InvalidOperationException(
                $"Scraper skrev ingen output-fil.\nStdout: {stdout}");

        var json = await File.ReadAllTextAsync(outFile);
        File.Delete(outFile);

        var courses = JsonSerializer.Deserialize<List<Course>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        return courses?.FirstOrDefault()
            ?? throw new InvalidOperationException("Scraper returnerade tom lista.");
    }
}
