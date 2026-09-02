using System.Net.Http.Json;
using System.Text.Json;

var baseUrl = args.Length > 0 ? args[0] : "http://localhost:5000";
var client = new HttpClient { BaseAddress = new Uri(baseUrl) };

Console.WriteLine("=== Kursklient ===\n");

try
{
    while (true)
    {
        Console.WriteLine("Välj åtgärd:");
        Console.WriteLine("  1. Visa alla kurser");
        Console.WriteLine("  2. Visa detaljerad kurs");
        Console.WriteLine("  3. Visa senaste veckan");
        Console.WriteLine("  4. Visa ej klara uppgifter");
        Console.WriteLine("  5. Visa klara uppgifter");
        Console.WriteLine("  0. Avsluta");
        Console.Write("\nVal: ");

        var choice = Console.ReadLine()?.Trim();
        Console.WriteLine();

        switch (choice)
        {
            case "1": await ListCoursesAsync(client); break;
            case "2": await ShowCourseDetailAsync(client); break;
            case "3": await ShowCurrentWeekAsync(client); break;
            case "4": await ShowIncompleteAsync(client); break;
            case "5": await ShowCompletedAsync(client); break;
            case "0" or null: return;
            default: Console.WriteLine("Okänt val.\n"); break;
        }
    }
}
catch (HttpRequestException ex)
{
    Console.WriteLine($"Kunde inte ansluta till {baseUrl}: {ex.Message}");
    Console.WriteLine("Kör coreservice först: dotnet run --project coreservice");
}

async Task ListCoursesAsync(HttpClient client)
{
    var courses = await client.GetFromJsonAsync<List<CourseSummary>>("/api/courses");
    if (courses is null || courses.Count == 0)
    {
        Console.WriteLine("Inga kurser hittades.\n");
        return;
    }

    Console.WriteLine($"{"ID",-5} {"Titel",-40} {"Framsteg",-12} {"Senast scrapad"}");
    Console.WriteLine(new string('-', 85));

    foreach (var c in courses)
    {
        Console.WriteLine($"{c.Id,-5} {Truncate(c.Title, 38),-40} {c.CompletionPercent,5:F1}%    {c.LastScrapedAt:yyyy-MM-dd HH:mm}");
    }
    Console.WriteLine();
}

async Task ShowCourseDetailAsync(HttpClient client)
{
    var id = AskForCourseId();
    var course = await client.GetFromJsonAsync<CourseDetail>($"/api/courses/{id}");
    if (course is null)
    {
        Console.WriteLine("Kurs hittades inte.\n");
        return;
    }

    Console.WriteLine($"Kurs: {course.Title}");
    Console.WriteLine($"Undertitel: {course.SubTitle}");
    Console.WriteLine($"Betyg: {course.Grade}");
    Console.WriteLine($"Senast scrapad: {course.LastScrapedAt:yyyy-MM-dd HH:mm}");
    Console.WriteLine();

    foreach (var section in course.Sections)
    {
        var done = section.Items.Count(i => i.Status == "Klar");
        var total = section.Items.Count;
        Console.WriteLine($"  [{done}/{total}] {section.Title}");

        foreach (var item in section.Items)
        {
            var icon = item.Status == "Klar" ? "✓" : "○";
            Console.WriteLine($"    {icon} {item.Title}");

            if (item.HasAiSummary && item.AiSummary is not null)
            {
                Console.WriteLine($"      Sammanfattning: {Truncate(item.AiSummary, 80)}");
            }
        }
        Console.WriteLine();
    }
}

async Task ShowCurrentWeekAsync(HttpClient client)
{
    var id = AskForCourseId();
    var week = await client.GetFromJsonAsync<CurrentWeek>($"/api/courses/{id}/current-week");
    if (week is null)
    {
        Console.WriteLine("Ingen vecka hittades.\n");
        return;
    }

    Console.WriteLine($"Vecka: {week.Section}");
    Console.WriteLine($"Beskrivning: {week.Description}\n");

    foreach (var item in week.Items)
    {
        var icon = item.Status == "Klar" ? "✓" : "○";
        Console.WriteLine($"  {icon} {item.Title}");

        if (item.HasAiSummary && item.AiSummary is not null)
        {
            Console.WriteLine($"    {item.AiSummary}\n");
        }
    }
    Console.WriteLine();
}

async Task ShowIncompleteAsync(HttpClient client)
{
    var id = AskForCourseId();
    var items = await client.GetFromJsonAsync<List<IncompleteItem>>($"/api/courses/{id}/incomplete");
    if (items is null || items.Count == 0)
    {
        Console.WriteLine("Alla uppgifter är klara!\n");
        return;
    }

    Console.WriteLine($"{items.Count} ej klara uppgifter:\n");
    foreach (var item in items)
    {
        Console.WriteLine($"  ○ {item.Title} ({item.Section})");
        if (item.Date.HasValue)
            Console.WriteLine($"    Deadline: {item.Date:yyyy-MM-dd}");

        if (item.HasAiSummary && item.AiSummary is not null)
            Console.WriteLine($"    {item.AiSummary}");
        Console.WriteLine();
    }
}

async Task ShowCompletedAsync(HttpClient client)
{
    var id = AskForCourseId();
    var items = await client.GetFromJsonAsync<List<CompletedItem>>($"/api/courses/{id}/completed");
    if (items is null || items.Count == 0)
    {
        Console.WriteLine("Inga avklarade uppgifter ännu.\n");
        return;
    }

    Console.WriteLine($"{items.Count} avklarade uppgifter:\n");
    foreach (var item in items)
    {
        Console.WriteLine($"  ✓ {item.Title} ({item.Section})");
    }
    Console.WriteLine();
}

int AskForCourseId()
{
    Console.Write("Ange kurs-ID: ");
    return int.TryParse(Console.ReadLine()?.Trim(), out var id) ? id : -1;
}

string Truncate(string text, int maxLen) =>
    text.Length <= maxLen ? text : text[..(maxLen - 3)] + "...";

// DTOs
record CourseSummary(int Id, int GroupId, string Title, string SubTitle, string Grade,
    DateTime LastScrapedAt, int TotalItems, int CompletedItems, double CompletionPercent, int SectionCount);

record CourseDetail(int Id, int GroupId, string Title, string SubTitle, string Grade,
    DateTime LastScrapedAt, List<SectionDto> Sections);

record SectionDto(int Id, string Title, string Description, List<ItemDto> Items);

record ItemDto(int Id, string ExternalItemId, string Title, string Status, DateTime? Date,
    DateTime ScrapedAt, bool HasAiSummary, string? AiSummary);

record CurrentWeek(string Section, string Description, List<WeekItemDto> Items);

record WeekItemDto(int Id, string Title, string Status, DateTime? Date, bool HasAiSummary, string? AiSummary);

record IncompleteItem(int Id, string Title, string Status, DateTime? Date, string Section,
    bool HasAiSummary, string? AiSummary);

record CompletedItem(int Id, string Title, string Status, DateTime? Date, string Section);
