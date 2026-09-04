using coreservice.Interfaces;
using coreservice.Models;

namespace coreservice.Events;

public record NewContentUploadedEvent(Course Course, DateTime UploadedAt) : IEvent;
