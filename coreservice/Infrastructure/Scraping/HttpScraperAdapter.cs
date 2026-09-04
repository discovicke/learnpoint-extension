using System.Net.Http.Json;
using System.Text.Json;
using coreservice.Interfaces;
using coreservice.Models;

namespace coreservice.Infrastructure.Scraping;

// Anropar scraper-service över HTTP (egen process på Scraper:BaseUrl).
// Stateless: scrapern minns inget mellan anrop - diffen ligger i CourseSyncHandler.
public class HttpScraperAdapter : IScraperService
{
    private readonly HttpClient _http;
    private readonly ILogger<HttpScraperAdapter> _logger;
    private readonly List<int> _registeredGroupIds;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public HttpScraperAdapter(HttpClient http, IConfiguration config, ILogger<HttpScraperAdapter> logger)
    {
        _http = http;
        _logger = logger;

        var baseUrl = config.GetValue<string>("Scraper:BaseUrl") ?? "http://localhost:5001";
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromMinutes(15);

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
            "[Scraper] POST {Base}scrape för {Count} grupper: {GroupIds}",
            _http.BaseAddress, ids.Count, string.Join(", ", ids));

        using var response = await _http.PostAsJsonAsync("scrape", new { groupIds = ids });
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("[Scraper] ✗ Scraper-service svarade {Status}: {Body}",
                (int)response.StatusCode, body);
            throw new InvalidOperationException($"Scraper-service svarade {(int)response.StatusCode}: {body}");
        }

        var courses = JsonSerializer.Deserialize<List<Course>>(body, JsonOptions) ?? [];

        _logger.LogInformation("[Scraper] ✓ Scraping klar: {Count} kurser hämtade", courses.Count);
        return courses;
    }

    private List<int> LoadRegisteredGroupIds()
    {
        try
        {
            var result = _http.GetFromJsonAsync<GroupList>("groups").GetAwaiter().GetResult();
            var ids = result?.GroupIds
                .Select(g => int.TryParse(g, out var id) ? (int?)id : null)
                .Where(i => i.HasValue)
                .Select(i => i!.Value)
                .ToList() ?? [];

            if (ids.Count == 0)
                _logger.LogWarning("[Scraper] ⚠ Scraper-service rapporterade inga registrerade grupper");
            else
                _logger.LogInformation("[Scraper]   Laddade {Count} registrerade grupper från scraper-service: {GroupIds}",
                    ids.Count, string.Join(", ", ids));

            return ids;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Scraper] ⚠ Kunde inte nå scraper-service ({Base}groups) - inga registrerade grupper",
                _http.BaseAddress);
            return [];
        }
    }

    private sealed record GroupList(List<string> GroupIds);
}
