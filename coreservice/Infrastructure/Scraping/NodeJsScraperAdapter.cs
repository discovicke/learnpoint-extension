using System.Diagnostics;
using System.Text.Json;
using coreservice.Application.Interfaces;
using coreservice.Domain.Models;

namespace coreservice.Infrastructure.Scraping;

public class NodeJsScraperAdapter : IScraperService
{
    private readonly string _scraperDir;
    private readonly string _nodeCommand;
    private readonly ILogger<NodeJsScraperAdapter> _logger;
    private readonly List<int> _registeredGroupIds;

    public NodeJsScraperAdapter(IConfiguration config, ILogger<NodeJsScraperAdapter> logger)
    {
        _logger = logger;
        _scraperDir = config.GetValue<string>("Scraper:WorkingDirectory")
            ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scraper-service");
        _nodeCommand = config.GetValue<string>("Scraper:NodeCommand") ?? "node";

        _registeredGroupIds = LoadRegisteredGroupIds();
    }

    public IReadOnlyList<int> RegisteredGroupIds => _registeredGroupIds;

    public async Task<Course> ScrapeCourseAsync(int groupId)
    {
        var courses = await ScrapeCoursesAsync(new[] { groupId });
        return courses.FirstOrDefault()
            ?? throw new InvalidOperationException("Scraper returnerade ingen kurs.");
    }

    public async Task<List<Course>> ScrapeCoursesAsync(IEnumerable<int> groupIds)
    {
        var ids = groupIds.Distinct().ToList();
        if (ids.Count == 0)
            throw new ArgumentException("Minst ett GroupId krävs.", nameof(groupIds));

        _logger.LogInformation(
            "[Scraper] → Startar Node.js deep-scraping för {Count} grupper: {GroupIds}",
            ids.Count, string.Join(", ", ids));

        var outFile = Path.Combine(Path.GetTempPath(), $"scraper-{Guid.NewGuid()}.json");

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

        psi.EnvironmentVariables["LEARNPOINT_GROUP_IDS"] = string.Join(",", ids);
        psi.EnvironmentVariables["OUT_FILE"] = outFile;
        psi.EnvironmentVariables["DEEP_SCRAPE"] = "true";

        var stopwatch = Stopwatch.StartNew();

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Kunde inte starta Node.js-processen");

        _logger.LogDebug("[Scraper]   Node.js-process startad (PID={PID})", process.Id);

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        stopwatch.Stop();

        if (process.ExitCode != 0)
        {
            _logger.LogError(
                "[Scraper] ✗ Scraper misslyckades (exit {ExitCode}, {Elapsed}ms):\n{Stderr}",
                process.ExitCode, stopwatch.ElapsedMilliseconds, stderr);
            throw new InvalidOperationException(
                $"Scraper misslyckades (exit {process.ExitCode}):\n{stderr}");
        }

        if (!File.Exists(outFile))
        {
            _logger.LogError(
                "[Scraper] ✗ Scraper skrev ingen output-fil ({Elapsed}ms)\nStdout: {Stdout}",
                stopwatch.ElapsedMilliseconds, stdout);
            throw new InvalidOperationException(
                $"Scraper skrev ingen output-fil.\nStdout: {stdout}");
        }

        _logger.LogInformation(
            "[Scraper]   Node.js-scraping klar ({Elapsed}ms)",
            stopwatch.ElapsedMilliseconds);

        var json = await File.ReadAllTextAsync(outFile);
        File.Delete(outFile);

        var courses = JsonSerializer.Deserialize<List<Course>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? [];

        _logger.LogInformation(
            "[Scraper] ✓ Deep-scraping klar: {Count} kurser hämtade",
            courses.Count);

        return courses;
    }

    private List<int> LoadRegisteredGroupIds()
    {
        var envFile = Path.Combine(_scraperDir, ".env");
        var ids = new List<int>();

        if (File.Exists(envFile))
        {
            foreach (var rawLine in File.ReadAllLines(envFile))
            {
                var line = rawLine.Trim();
                if (!line.StartsWith("LEARNPOINT_GROUP_IDS", StringComparison.OrdinalIgnoreCase))
                    continue;

                var value = line.Split('=', 2) is [_, var v] ? v.Trim() : "";
                foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (int.TryParse(part, out var id))
                        ids.Add(id);
                }
                break;
            }
        }

        if (ids.Count == 0)
            _logger.LogWarning(
                "[Scraper] ⚠ Kunde inte läsa LEARNPOINT_GROUP_IDS från {EnvFile} — inga registrerade grupper",
                envFile);
        else
            _logger.LogInformation(
                "[Scraper]   Laddade {Count} registrerade grupper från .env: {GroupIds}",
                ids.Count, string.Join(", ", ids));

        return ids;
    }
}
