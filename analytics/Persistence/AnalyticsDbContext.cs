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
            entity.ToTable("page_views");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OccurredAtUtc);
            entity.HasIndex(x => x.Path);
            entity.HasIndex(x => x.VisitorHash);
            entity.HasIndex(x => x.Country);
            entity.HasIndex(x => x.ClientIp);

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.OccurredAtUtc)
                .HasColumnName("occurred_at_utc")
                .HasColumnType("timestamp with time zone");
            entity.Property(x => x.Path).HasColumnName("path").HasMaxLength(200).IsRequired();
            entity.Property(x => x.Referrer).HasColumnName("referrer").HasMaxLength(500);
            entity.Property(x => x.UserAgent).HasColumnName("user_agent").HasMaxLength(400);
            entity.Property(x => x.VisitorHash).HasColumnName("visitor_hash").HasMaxLength(32);
            entity.Property(x => x.ClientIp).HasColumnName("client_ip").HasMaxLength(64);
            entity.Property(x => x.Country).HasColumnName("country").HasMaxLength(8);
            entity.Property(x => x.Region).HasColumnName("region").HasMaxLength(80);
            entity.Property(x => x.City).HasColumnName("city").HasMaxLength(80);
            entity.Property(x => x.PostalCode).HasColumnName("postal_code").HasMaxLength(24);
            entity.Property(x => x.Latitude).HasColumnName("latitude");
            entity.Property(x => x.Longitude).HasColumnName("longitude");
            entity.Property(x => x.Asn).HasColumnName("asn");
            entity.Property(x => x.Org).HasColumnName("org").HasMaxLength(120);
            entity.Property(x => x.Isp).HasColumnName("isp").HasMaxLength(120);
            entity.Property(x => x.Language).HasColumnName("language").HasMaxLength(32);
            entity.Property(x => x.Screen).HasColumnName("screen").HasMaxLength(32);
            entity.Property(x => x.Browser).HasColumnName("browser").HasMaxLength(64);
            entity.Property(x => x.Os).HasColumnName("os").HasMaxLength(64);
            entity.Property(x => x.UtmSource).HasColumnName("utm_source").HasMaxLength(120);
            entity.Property(x => x.UtmMedium).HasColumnName("utm_medium").HasMaxLength(120);
            entity.Property(x => x.UtmCampaign).HasColumnName("utm_campaign").HasMaxLength(120);
            entity.Property(x => x.UtmContent).HasColumnName("utm_content").HasMaxLength(120);
            entity.Property(x => x.UtmTerm).HasColumnName("utm_term").HasMaxLength(120);
        });
    }

    /// <summary>
    /// EnsureCreated does not add columns to an existing database — patch missing ones (Postgres).
    /// </summary>
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await Database.EnsureCreatedAsync(cancellationToken);

        var columns = new (string Name, string SqlType)[]
        {
            ("language", "text"),
            ("screen", "text"),
            ("browser", "text"),
            ("os", "text"),
            ("utm_source", "text"),
            ("utm_medium", "text"),
            ("utm_campaign", "text"),
            ("utm_content", "text"),
            ("utm_term", "text"),
            ("client_ip", "text"),
            ("visitor_hash", "text"),
            ("country", "text"),
            ("region", "text"),
            ("city", "text"),
            ("postal_code", "text"),
            ("latitude", "double precision"),
            ("longitude", "double precision"),
            ("asn", "integer"),
            ("org", "text"),
            ("isp", "text"),
            ("referrer", "text"),
            ("user_agent", "text"),
        };

        foreach (var (name, sqlType) in columns)
        {
            // Column names are fixed literals from the array above (not user input).
#pragma warning disable EF1002
            await Database.ExecuteSqlRawAsync(
                $"ALTER TABLE page_views ADD COLUMN IF NOT EXISTS {name} {sqlType};",
                cancellationToken);
#pragma warning restore EF1002
        }
    }
}
