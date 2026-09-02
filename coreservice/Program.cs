using coreservice.Application.Interfaces;
using coreservice.Infrastructure.Events;
using coreservice.Application.Interfaces;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddSingleton<EventBus>();
builder.Services.AddSingleton<IEventPublisher, EventBus>();


var app = builder.Build();

app.MapGet("/health", () => "OK!");

app.Run("http://0.0.0.0:5000");