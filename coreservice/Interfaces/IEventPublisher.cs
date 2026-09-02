namespace coreservice.Application.Interfaces;

public interface IEventPublisher
{
    Task Publish<TEvent>(TEvent @event) where TEvent : IEvent;
}
