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
builder.Services.AddSingleton<IEventPublisher, EventBus>();
builder.Services.AddSingleton<IScraperService, NodeJsScraperAdapter>();
builder.Services.AddHttpClient<IAiSummarizeService, GeminiSummarizeAdapter>();
builder.Services.AddSingleton<IEventHandler<NewContentUploadedEvent>, AiSummarizeHandler>();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

var eventBus = app.Services.GetRequiredService<EventBus>();
var summarizeHandler = app.Services.GetRequiredService<IEventHandler<NewContentUploadedEvent>>();
eventBus.Subscribe(summarizeHandler);

app.MapGet("/health", () => "OK!");
app.MapTriggerEndpoints();
app.MapCourseEndpoints();

app.Run("http://0.0.0.0:5000");
