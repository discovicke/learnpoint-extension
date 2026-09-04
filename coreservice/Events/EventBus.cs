using System.Collections.Concurrent;
using coreservice.Interfaces;

namespace coreservice.Events;

public class EventBus : IEventPublisher
{
    private readonly ConcurrentDictionary<Type, List<IEventHandler>> handlers = new();
    private readonly ILogger<EventBus> _logger;

    public EventBus(ILogger<EventBus> logger)
    {
        _logger = logger;
    }

    public void Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent
    {
        var eventType = typeof(TEvent);
        handlers.GetOrAdd(eventType, _ => new List<IEventHandler>()).Add(handler);
        _logger.LogInformation("[EventBus] Prenumerant tillagd: {Handler} för {Event} - totalt {Count}",
            handler.GetType().Name, eventType.Name,
            handlers.TryGetValue(eventType, out var list) ? list.Count : 0);
    }

    public void Unsubscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent
    {
        var eventType = typeof(TEvent);
        if (handlers.TryGetValue(eventType, out var value))
        {
            value.Remove(handler);
            _logger.LogDebug("[EventBus] Prenumerant borttagen: {Handler} från {Event}",
                handler.GetType().Name, eventType.Name);
        }
    }

    public async Task Publish<TEvent>(TEvent @event) where TEvent : IEvent
    {
        var eventType = typeof(TEvent);
        if (!handlers.TryGetValue(eventType, out var value))
        {
            _logger.LogWarning("[EventBus] ⚠ Inga prenumeranter för {Event}", eventType.Name);
            return;
        }

        _logger.LogInformation("[EventBus] Publicerar {Event} till {Count} handlare",
            eventType.Name, value.Count);

        foreach (var handler in value.ToList())
        {
            _logger.LogDebug("[EventBus]   Anropar {Handler}", handler.GetType().Name);
            await ((IEventHandler<TEvent>)handler).Handle(@event);
        }

        _logger.LogInformation("[EventBus] ✓ {Event} hanterad", eventType.Name);
    }
}
