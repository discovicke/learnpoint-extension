using coreservice.Application.Interfaces;

namespace coreservice.Application.Events;

public record SectionRegisteredEvent(int CourseId, int SectionId) : IEvent;
