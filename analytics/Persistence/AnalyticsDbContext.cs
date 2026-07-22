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
            entity.HasIndex(x => x.Country);
            entity.HasIndex(x => x.ClientIp);
            entity.Property(x => x.Path).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Referrer).HasMaxLength(500);
            entity.Property(x => x.UserAgent).HasMaxLength(400);
            entity.Property(x => x.VisitorHash).HasMaxLength(32);
            entity.Property(x => x.ClientIp).HasMaxLength(64);
            entity.Property(x => x.Country).HasMaxLength(8);
            entity.Property(x => x.Language).HasMaxLength(32);
            entity.Property(x => x.Screen).HasMaxLength(32);
            entity.Property(x => x.Browser).HasMaxLength(64);
            entity.Property(x => x.Os).HasMaxLength(64);
            entity.Property(x => x.UtmSource).HasMaxLength(120);
            entity.Property(x => x.UtmMedium).HasMaxLength(120);
            entity.Property(x => x.UtmCampaign).HasMaxLength(120);
            entity.Property(x => x.UtmContent).HasMaxLength(120);
            entity.Property(x => x.UtmTerm).HasMaxLength(120);
        });
    }

    /// <summary>
    /// EnsureCreated does not add columns to an existing SQLite DB — patch missing ones.
    /// </summary>
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await Database.EnsureCreatedAsync(cancellationToken);

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = Database.GetDbConnection().CreateCommand())
        {
            await Database.OpenConnectionAsync(cancellationToken);
            command.CommandText = "PRAGMA table_info('PageViews');";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                existing.Add(reader.GetString(1));
            }
        }

        var columns = new (string Name, string SqlType)[]
        {
            ("Language", "TEXT"),
            ("Screen", "TEXT"),
            ("Browser", "TEXT"),
            ("Os", "TEXT"),
            ("UtmSource", "TEXT"),
            ("UtmMedium", "TEXT"),
            ("UtmCampaign", "TEXT"),
            ("UtmContent", "TEXT"),
            ("UtmTerm", "TEXT"),
            ("ClientIp", "TEXT"),
        };

        foreach (var (name, sqlType) in columns)
        {
            if (existing.Contains(name))
            {
                continue;
            }

            // Column names are fixed literals from the array above (not user input).
#pragma warning disable EF1002
            await Database.ExecuteSqlRawAsync(
                $"ALTER TABLE PageViews ADD COLUMN {name} {sqlType};",
                cancellationToken);
#pragma warning restore EF1002
        }
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
    public string? ClientIp { get; set; }
    public string? Country { get; set; }
    public string? Language { get; set; }
    public string? Screen { get; set; }
    public string? Browser { get; set; }
    public string? Os { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public string? UtmContent { get; set; }
    public string? UtmTerm { get; set; }
}
