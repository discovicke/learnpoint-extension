namespace coreservice.Interfaces;

public interface IEventHandler
{
}

public interface IEventHandler<T> : IEventHandler where T : IEvent
{
    Task Handle(T @event);
}