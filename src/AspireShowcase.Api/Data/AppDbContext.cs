using Microsoft.EntityFrameworkCore;

class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public const int MaxNameLength = 100;
    public const int MaxSlugLength = 40;

    // Named so a unique violation can tell which rule was broken.
    public const string SlugIndex = "IX_Businesses_Slug";
    public const string OwnerIndex = "IX_Businesses_OwnerId";

    public DbSet<Business> Businesses => Set<Business>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Business>(business =>
        {
            business.Property(b => b.Name).HasMaxLength(MaxNameLength);
            business.Property(b => b.Slug).HasMaxLength(MaxSlugLength);
            business.Property(b => b.OwnerId).HasMaxLength(64);
            // The database is what guarantees both, even when two requests race.
            business.HasIndex(b => b.Slug).IsUnique().HasDatabaseName(SlugIndex);
            business.HasIndex(b => b.OwnerId).IsUnique().HasDatabaseName(OwnerIndex);
        });
}
