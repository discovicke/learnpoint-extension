using System.Net.Http.Json;
using System.Text.Json;

var baseUrl = args.Length > 0 ? args[0] : "http://localhost:5000";
var client = new HttpClient { BaseAddress = new Uri(baseUrl) };

Console.WriteLine("=== Kursklient ===\n");

try
{
    while (true)
    {
        ShowMenu();

        var choice = Console.ReadLine()?.Trim();
        Console.Clear();

        switch (choice)
        {
            case "1": await RunWithPause("1. Visa alla kurser", () => ListCoursesAsync(client)); break;
            case "2": await RunWithPause("2. Visa detaljerad kurs", () => ShowCourseDetailAsync(client)); break;
            case "3": await RunWithPause("3. Visa senaste veckan", () => ShowCurrentWeekAsync(client)); break;
            case "4": await RunWithPause("4. Visa ej klara uppgifter", () => ShowIncompleteAsync(client)); break;
            case "5": await RunWithPause("5. Visa klara uppgifter", () => ShowCompletedAsync(client)); break;
            case "6": await RunWithPause("6. Trigga scraping för grupp", () => TriggerGroupAsync(client)); break;
            case "7": await RunWithPause("7. Trigga scraping för alla grupper", () => TriggerAllAsync(client)); break;
            case "8": await RunWithPause("8. Sammanfatta sektion manuellt", () => SummarizeSectionAsync(client)); break;
            case "9": await RunWithPause("9. Lista prenumeranter", () => ListSubscribersAsync(client)); break;
            case "10": await RunWithPause("10. Lägg till prenumerant", () => AddSubscriberAsync(client)); break;
            case "11": await RunWithPause("11. Ta bort prenumerant", () => DeleteSubscriberAsync(client)); break;
            case "0" or null: return;
            default:
                Console.WriteLine("Okänt val.\n");
                Pause();
                break;
        }
    }
}
catch (HttpRequestException ex)
{
    Console.WriteLine($"Kunde inte ansluta till {baseUrl}: {ex.Message}");
    Console.WriteLine("Kör coreservice först: dotnet run --project coreservice");
}

void ShowMenu()
{
    Console.Clear();
    Console.WriteLine("=== Kursklient ===\n");
    Console.WriteLine("[Läsning]");
    Console.WriteLine("  1. Visa alla kurser");
    Console.WriteLine("  2. Visa detaljerad kurs");
    Console.WriteLine("  3. Visa senaste veckan");
    Console.WriteLine("  4. Visa ej klara uppgifter");
    Console.WriteLine("  5. Visa klara uppgifter");
    Console.WriteLine();
    Console.WriteLine("[Trigger & AI]");
    Console.WriteLine("  6. Trigga scraping för grupp");
    Console.WriteLine("  7. Trigga scraping för alla grupper");
    Console.WriteLine("  8. Sammanfatta sektion manuellt");
    Console.WriteLine();
    Console.WriteLine("[Prenumeranter]");
    Console.WriteLine("  9. Lista prenumeranter");
    Console.WriteLine(" 10. Lägg till prenumerant");
    Console.WriteLine(" 11. Ta bort prenumerant");
    Console.WriteLine();
    Console.WriteLine("  0. Avsluta");
    Console.Write("\nVal: ");
}

async Task RunWithPause(string header, Func<Task> action)
{
    Console.WriteLine($"--- {header} ---\n");
    try
    {
        await action();
    }
    catch (HttpRequestException ex)
    {
        Console.WriteLine($"Anropet misslyckades: {ex.Message}\n");
    }
    Pause();
}

void Pause()
{
    Console.WriteLine("Tryck Enter för att återgå...");
    Console.ReadLine();
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
        Console.WriteLine($"  [{section.Id}] [{done}/{total}] {section.Title}");

        if (section.HasAiSummary && section.AiSummary is not null)
        {
            Console.WriteLine($"    Veckosammanfattning: {Truncate(section.AiSummary, 120)}");
        }
        if (section.SummaryFile is not null)
        {
            Console.WriteLine($"    Fil: {section.SummaryFile}");
        }

        foreach (var item in section.Items)
        {
            var icon = item.Status == "Klar" ? "✓" : "○";
            Console.WriteLine($"    {icon} {item.Title}");
        }
        Console.WriteLine();
    }
}

async Task ShowCurrentWeekAsync(HttpClient client)
{
    var id = AskForCourseId();
    CurrentWeek? week;
    try
    {
        week = await client.GetFromJsonAsync<CurrentWeek>($"/api/courses/{id}/current-week");
    }
    catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
    {
        Console.WriteLine($"Kunde inte hämta senaste veckan: {ex.Message}\n");
        return;
    }
    if (week is null)
    {
        Console.WriteLine("Ingen vecka hittades.\n");
        return;
    }

    Console.WriteLine($"Vecka: {week.Section}");
    Console.WriteLine($"Beskrivning: {week.Description}\n");

    if (week.HasAiSummary && week.AiSummary is not null)
    {
        Console.WriteLine($"Veckosammanfattning:\n{week.AiSummary}\n");
    }
    if (week.SummaryFile is not null)
    {
        Console.WriteLine($"Fil: {week.SummaryFile}\n");
    }

    foreach (var item in week.Items)
    {
        var icon = item.Status == "Klar" ? "✓" : "○";
        Console.WriteLine($"  {icon} {item.Title}");
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

async Task TriggerGroupAsync(HttpClient client)
{
    Console.Write("Ange grupp-ID (Learnpoint): ");
    if (!int.TryParse(Console.ReadLine()?.Trim(), out var groupId))
    {
        Console.WriteLine("Ogiltigt grupp-ID.\n");
        return;
    }

    using var response = await client.PostAsync($"/api/trigger/{groupId}", null);
    if (!await EnsureOk(response)) return;

    var result = await response.Content.ReadFromJsonAsync<TriggerSingleResponse>();
    Console.WriteLine($"{result?.Message}");
    Console.WriteLine($"Kurs: {result?.GroupTitle} ({result?.Sections} sektioner, {result?.Items} items)\n");
}

async Task TriggerAllAsync(HttpClient client)
{
    using var response = await client.PostAsync("/api/trigger/all", null);
    if (!await EnsureOk(response)) return;

    var result = await response.Content.ReadFromJsonAsync<TriggerAllResponse>();
    Console.WriteLine($"{result?.Message}");
    Console.WriteLine($"Begärda: {result?.TotalRequested}, scrapade: {result?.TotalScraped}, synkade: {result?.TotalSynced}\n");
}

async Task SummarizeSectionAsync(HttpClient client)
{
    Console.Write("Ange sektions-ID (se kursdetalj för ID): ");
    if (!int.TryParse(Console.ReadLine()?.Trim(), out var sectionId))
    {
        Console.WriteLine("Ogiltigt sektions-ID.\n");
        return;
    }

    using var response = await client.PostAsync($"/api/sections/{sectionId}/summarize", null);
    if (!await EnsureOk(response)) return;

    var result = await response.Content.ReadFromJsonAsync<SummarizeResponse>();
    Console.WriteLine($"Sammanfattning beställd för '{result?.Title}' (ID={result?.SectionId}).\n");
}

async Task ListSubscribersAsync(HttpClient client)
{
    var subs = await client.GetFromJsonAsync<List<SubscriberDto>>("/api/subscribers");
    if (subs is null || subs.Count == 0)
    {
        Console.WriteLine("Inga prenumeranter registrerade.\n");
        return;
    }

    Console.WriteLine($"{"ID",-5} {"Nummer",-18} {"Namn"}");
    Console.WriteLine(new string('-', 50));
    foreach (var s in subs)
        Console.WriteLine($"{s.Id,-5} {s.PhoneNumber,-18} {s.Name}");
    Console.WriteLine();
}

async Task AddSubscriberAsync(HttpClient client)
{
    Console.Write("Telefonnummer (E.164, t.ex. +46701234567): ");
    var phone = Console.ReadLine()?.Trim() ?? "";
    Console.Write("Namn (valfritt): ");
    var name = Console.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(name)) name = null;

    using var response = await client.PostAsJsonAsync("/api/subscribers", new { phoneNumber = phone, name });
    if (!await EnsureOk(response)) return;

    var result = await response.Content.ReadFromJsonAsync<SubscriberDto>();
    Console.WriteLine($"Tillagd: {result?.PhoneNumber} (ID={result?.Id}).\n");
}

async Task DeleteSubscriberAsync(HttpClient client)
{
    Console.Write("Ange prenumerant-ID: ");
    if (!int.TryParse(Console.ReadLine()?.Trim(), out var id))
    {
        Console.WriteLine("Ogiltigt ID.\n");
        return;
    }

    using var response = await client.DeleteAsync($"/api/subscribers/{id}");
    if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
    {
        Console.WriteLine("Prenumerant borttagen.\n");
        return;
    }
    await EnsureOk(response);
}

async Task<bool> EnsureOk(HttpResponseMessage response)
{
    if (response.IsSuccessStatusCode) return true;
    var message = await ReadErrorMessage(response);
    Console.WriteLine($"Fel {(int)response.StatusCode}: {message}\n");
    return false;
}

async Task<string> ReadErrorMessage(HttpResponseMessage response)
{
    try
    {
        var err = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        if (!string.IsNullOrWhiteSpace(err?.Message)) return err.Message;
    }
    catch (Exception) { /* fall igenom till råtext */ }
    try
    {
        var raw = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(raw) ? response.ReasonPhrase ?? "okänt fel" : raw;
    }
    catch (Exception)
    {
        return response.ReasonPhrase ?? "okänt fel";
    }
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

record SectionDto(int Id, string Title, string Description, bool HasAiSummary, string? AiSummary, DateTime? SummarizedAt, string? SummaryFile, List<ItemDto> Items);

record ItemDto(int Id, string ExternalItemId, string Title, string Status, DateTime? Date,
    DateTime ScrapedAt);

record CurrentWeek(string Section, string Description, bool HasAiSummary, string? AiSummary, DateTime? SummarizedAt, string? SummaryFile, List<WeekItemDto> Items);

record WeekItemDto(int Id, string Title, string Status, DateTime? Date);

record IncompleteItem(int Id, string Title, string Status, DateTime? Date, string Section);

record CompletedItem(int Id, string Title, string Status, DateTime? Date, string Section);

record TriggerSingleResponse(string Message, string GroupTitle, int Sections, int Items);

record TriggerAllResponse(string Message, int TotalRequested, int TotalScraped, int TotalSynced);

record SummarizeResponse(int SectionId, string Title);

record SubscriberDto(int Id, string PhoneNumber, string? Name, DateTime CreatedAt);

record ErrorResponse(string? Message);
