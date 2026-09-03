using coreservice.Application.Interfaces;

namespace coreservice.Application.Events;

public record WeekSummarizedEvent(int CourseId, int SectionId, string Title, string AiSummary) : IEvent;
