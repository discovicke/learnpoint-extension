using coreservice.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace coreservice.Infrastructure.Events;

public class AppDbContext : DbContext
{
    public DbSet<TrackedCourse> Courses => Set<TrackedCourse>();
    public DbSet<TrackedSection> Sections => Set<TrackedSection>();
    public DbSet<TrackedItem> Items => Set<TrackedItem>();
    
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.UseSqlite("Data Source=coreservice.db");
}