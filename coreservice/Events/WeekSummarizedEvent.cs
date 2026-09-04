using coreservice.Interfaces;

namespace coreservice.Events;

public record WeekSummarizedEvent(int CourseId, int SectionId, string Title, string AiSummary) : IEvent;
