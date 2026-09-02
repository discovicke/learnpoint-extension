using coreservice.Application.Interfaces;
using coreservice.Endpoints;
using coreservice.Infrastructure.Events;
using coreservice.Infrastructure.Scraping;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<EventBus>();
builder.Services.AddSingleton<IEventPublisher, EventBus>();
builder.Services.AddSingleton<IScraperService, NodeJsScraperAdapter>();

var app = builder.Build();

app.MapGet("/health", () => "OK!");
app.MapTriggerEndpoints();

app.Run("http://0.0.0.0:5000");
