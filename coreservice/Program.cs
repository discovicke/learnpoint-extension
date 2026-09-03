using coreservice.Application.Events;
using coreservice.Application.Interfaces;
using coreservice.Endpoints;
using coreservice.Handlers;
using coreservice.Infrastructure.Ai;
using coreservice.Infrastructure.Data;
using coreservice.Infrastructure.Events;
using coreservice.Infrastructure.Scraping;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<EventBus>();
builder.Services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<EventBus>());
builder.Services.AddSingleton<IScraperService, NodeJsScraperAdapter>();
builder.Services.AddHttpClient<IAiSummarizeService, GeminiSummarizeAdapter>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IEventHandler<NewContentUploadedEvent>, CourseSyncHandler>();
builder.Services.AddSingleton<IEventHandler<SectionRegisteredEvent>, AiSummarizeHandler>();
builder.Services.AddSingleton<IEventHandler<WeekSummarizedEvent>, SmsHandler>();
builder.Services.AddSingleton<IEventHandler<WeekSummarizedEvent>, BuggernautHandler>();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    EnsureSectionSummaryColumns(db);
    EnsureSubscribersTable(db);
}

var eventBus = app.Services.GetRequiredService<EventBus>();

var syncHandler = app.Services.GetRequiredService<IEventHandler<NewContentUploadedEvent>>();
var summarizeHandler = app.Services.GetRequiredService<IEventHandler<SectionRegisteredEvent>>();
var smsHandler = app.Services.GetServices<IEventHandler<WeekSummarizedEvent>>().OfType<SmsHandler>().Single();
var buggernautHandler = app.Services.GetServices<IEventHandler<WeekSummarizedEvent>>().OfType<BuggernautHandler>().Single();

eventBus.Subscribe(syncHandler);
eventBus.Subscribe(summarizeHandler);
// SMS först, Buggernaut sedan — båda får aldrig kasta (try/catch internt),
// så den ena påverkar aldrig den andra.
eventBus.Subscribe(smsHandler);
eventBus.Subscribe(buggernautHandler);

app.MapGet("/health", () => "OK!");
app.MapTriggerEndpoints();
app.MapCourseEndpoints();
app.MapSubscriberEndpoints();

app.Run("http://0.0.0.0:5000");

// EnsureCreated migrerar inte befintliga databaser — lägg till nya
// kolumner/tabeller manuellt så gamla db-filer inte kraschar.
static void EnsureSectionSummaryColumns(AppDbContext db)
{
    var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var row in db.Database.SqlQueryRaw<ColumnInfo>("SELECT name FROM pragma_table_info('Sections')"))
        columns.Add(row.name);

    if (!columns.Contains("AiSummary"))
        db.Database.ExecuteSqlRaw("ALTER TABLE Sections ADD COLUMN AiSummary TEXT");

    if (!columns.Contains("SummarizedAt"))
        db.Database.ExecuteSqlRaw("ALTER TABLE Sections ADD COLUMN SummarizedAt TEXT");
}

static void EnsureSubscribersTable(AppDbContext db)
{
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS Subscribers (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            PhoneNumber TEXT NOT NULL,
            Name TEXT NULL,
            CreatedAt TEXT NOT NULL
        )
        """);
    db.Database.ExecuteSqlRaw(
        "CREATE UNIQUE INDEX IF NOT EXISTS IX_Subscribers_PhoneNumber ON Subscribers (PhoneNumber)");
}

sealed class ColumnInfo
{
    public string name { get; set; } = "";
}
