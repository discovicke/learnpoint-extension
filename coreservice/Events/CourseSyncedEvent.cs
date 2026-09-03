using coreservice.Application.Interfaces;

namespace coreservice.Application.Events;

public record CourseSyncedEvent(int CourseId, int GroupId, string Title, int NewItems, int UpdatedItems, DateTime SyncedAt) : IEvent;
