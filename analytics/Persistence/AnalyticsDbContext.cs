using Microsoft.EntityFrameworkCore;

namespace AnalyticsApi.Persistence;

public sealed class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options)
    : DbContext(options)
{
    public DbSet<PageView> PageViews => Set<PageView>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PageView>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OccurredAtUtc);
            entity.HasIndex(x => x.Path);
            entity.HasIndex(x => x.VisitorHash);
            entity.Property(x => x.Path).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Referrer).HasMaxLength(500);
            entity.Property(x => x.UserAgent).HasMaxLength(400);
            entity.Property(x => x.VisitorHash).HasMaxLength(32);
            entity.Property(x => x.Country).HasMaxLength(8);
        });
    }
}

public sealed class PageView
{
    public long Id { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string Path { get; set; } = "/";
    public string? Referrer { get; set; }
    public string? UserAgent { get; set; }
    public string? VisitorHash { get; set; }
    public string? Country { get; set; }
}
