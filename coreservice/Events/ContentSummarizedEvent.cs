using coreservice.Application.Interfaces;

namespace coreservice.Application.Events;

public record ContentSummarizedEvent(int CourseId, int ItemId, string Summary, DateTime SummarizedAt) : IEvent;