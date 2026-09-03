using coreservice.Application.Interfaces;

namespace coreservice.Application.Events;

public record SummarizeCourseEvent(int CourseId, string Title) : IEvent;
