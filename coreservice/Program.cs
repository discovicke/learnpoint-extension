using coreservice.Application.Events;
using coreservice.Application.Interfaces;
using coreservice.Endpoints;
using coreservice.Handlers;
using coreservice.Infrastructure.Ai;
using coreservice.Infrastructure.Buggernaut;
using coreservice.Infrastructure.Data;
using coreservice.Infrastructure.Events;
using coreservice.Infrastructure.Logging;
using coreservice.Infrastructure.Scraping;
using coreservice.Infrastructure.Summaries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Console;

var builder = WebApplication.CreateBuilder(args);

Console.OutputEncoding = System.Text.Encoding.UTF8;

builder.Logging.AddConsole(options => options.FormatterName = "brief");
builder.Logging.AddConsoleFormatter<BriefConsoleFormatter, ConsoleFormatterOptions>();

builder.Services.AddSingleton<EventBus>();
builder.Services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<EventBus>());
builder.Services.AddHttpClient<IScraperService, HttpScraperAdapter>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(15);
});
builder.Services.AddHttpClient<IAiSummarizeService, GeminiSummarizeAdapter>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ISectionSummaryStore, FileSectionSummaryStore>();
builder.Services.AddSingleton<IBuggernautService, ProcessBuggernautAdapter>();
builder.Services.AddSingleton<IEventHandler<NewContentUploadedEvent>, CourseSyncHandler>();
builder.Services.AddSingleton<IEventHandler<SectionRegisteredEvent>, AiSummarizeHandler>();
builder.Services.AddSingleton<IEventHandler<WeekSummarizedEvent>, SummaryFileHandler>();
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
var summaryFileHandler = app.Services.GetServices<IEventHandler<WeekSummarizedEvent>>().OfType<SummaryFileHandler>().Single();

eventBus.Subscribe(syncHandler);
eventBus.Subscribe(summarizeHandler);
eventBus.Subscribe(summaryFileHandler);
eventBus.Subscribe(smsHandler);
eventBus.Subscribe(buggernautHandler);

app.MapGet("/health", () => "OK!");
app.MapTriggerEndpoints();
app.MapCourseEndpoints();
app.MapSubscriberEndpoints();

app.Run("http://0.0.0.0:5000");

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
