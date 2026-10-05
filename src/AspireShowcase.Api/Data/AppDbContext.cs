using AspireShowcase.Api.BusinessSetup;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// app-db, shared by the API's modules. Each module maps its own aggregates, in its
/// Infrastructure folder.
/// </summary>
class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Business> Businesses => Set<Business>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
