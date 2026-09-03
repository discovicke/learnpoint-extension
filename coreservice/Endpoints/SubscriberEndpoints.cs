using System.Text.RegularExpressions;
using coreservice.Domain.Models;
using coreservice.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace coreservice.Endpoints;

public static class SubscriberEndpoints
{
    public static void MapSubscriberEndpoints(this WebApplication app)
    {
        app.MapGet("/api/subscribers", async (AppDbContext db) =>
            await db.Subscribers
                .OrderBy(s => s.Id)
                .Select(s => new { s.Id, s.PhoneNumber, s.Name, s.CreatedAt })
                .ToListAsync());

        app.MapPost("/api/subscribers", async (SubscriberRequest request, AppDbContext db, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Endpoint.Subscribers");

            if (!IsValidE164(request.PhoneNumber))
            {
                logger.LogWarning("[Subscribers] ⚠ Ogiltigt telefonnummer: '{Phone}'", request.PhoneNumber);
                return Results.BadRequest(new { message = "Telefonnummer måste vara E.164-format, t.ex. +46701234567." });
            }

            if (await db.Subscribers.AnyAsync(s => s.PhoneNumber == request.PhoneNumber))
                return Results.Conflict(new { message = "Numret är redan registrerat." });

            var subscriber = new Subscriber
            {
                PhoneNumber = request.PhoneNumber,
                Name = request.Name,
                CreatedAt = DateTime.UtcNow,
            };
            db.Subscribers.Add(subscriber);
            await db.SaveChangesAsync();

            logger.LogInformation("[Subscribers] ✓ Prenumerant tillagd: {Phone} (ID={Id})", subscriber.PhoneNumber, subscriber.Id);
            return Results.Created($"/api/subscribers/{subscriber.Id}",
                new { subscriber.Id, subscriber.PhoneNumber, subscriber.Name });
        });

        app.MapDelete("/api/subscribers/{id:int}", async (int id, AppDbContext db) =>
        {
            var subscriber = await db.Subscribers.FindAsync(id);
            if (subscriber is null)
                return Results.NotFound();

            db.Subscribers.Remove(subscriber);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static bool IsValidE164(string? phone) =>
        !string.IsNullOrWhiteSpace(phone) && Regex.IsMatch(phone, @"^\+\d{7,15}$");

    public sealed record SubscriberRequest(string PhoneNumber, string? Name);
}
