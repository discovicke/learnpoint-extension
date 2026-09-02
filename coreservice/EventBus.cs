using System.Collections.Concurrent;
using coreservice.Application.Interfaces;

namespace coreservice.Infrastructure.Events;

public class EventBus : IEventPublisher
{
    private readonly ConcurrentDictionary<Type, List<IEventHandler>> handlers = new();

    public void Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent
    {
        var eventType = typeof(TEvent);
        handlers.GetOrAdd(eventType, _ => new List<IEventHandler>()).Add(handler);
    }

    public void Unsubscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent
    {
        var eventType = typeof(TEvent);
        if (handlers.TryGetValue(eventType, out var value))
            value.Remove(handler);
        
    }
    
    public async Task Publish<TEvent>(TEvent @event) where TEvent : IEvent
    {
        var eventType = typeof(TEvent);
        if (!handlers.TryGetValue(eventType, out var value)) 
            return;
        foreach (var handler in value.ToList())
        {
            await ((IEventHandler<TEvent>)handler).Handle(@event);
        }
    }
}