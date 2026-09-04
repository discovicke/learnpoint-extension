using coreservice.Interfaces;

namespace coreservice.Events;

public record SectionRegisteredEvent(int CourseId, int SectionId) : IEvent;
