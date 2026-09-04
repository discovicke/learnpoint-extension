using coreservice.Models;
using Microsoft.EntityFrameworkCore;

namespace coreservice.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public DbSet<TrackedCourse> Courses => Set<TrackedCourse>();
    public DbSet<TrackedSection> Sections => Set<TrackedSection>();
    public DbSet<TrackedItem> Items => Set<TrackedItem>();
    public DbSet<Subscriber> Subscribers => Set<Subscriber>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TrackedCourse>(e =>
        {
            e.HasIndex(c => c.GroupId).IsUnique();
            e.HasMany(c => c.Sections)
             .WithOne(s => s.TrackedCourse)
             .HasForeignKey(s => s.TrackedCourseId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrackedSection>(e =>
        {
            e.HasMany(s => s.Items)
             .WithOne(i => i.TrackedSection)
             .HasForeignKey(i => i.TrackedSectionId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrackedItem>(e =>
        {
            e.HasIndex(i => i.ExternalItemId);
        });

        modelBuilder.Entity<Subscriber>(e =>
        {
            e.HasIndex(s => s.PhoneNumber).IsUnique();
        });
    }
}
