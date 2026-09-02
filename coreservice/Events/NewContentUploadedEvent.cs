using coreservice.Application.Interfaces;
using coreservice.Domain.Models;

namespace coreservice.Application.Events;

public record NewContentUploadedEvent(Course Course, DateTime UploadedAt) : IEvent;
