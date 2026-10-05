using Microsoft.EntityFrameworkCore;

namespace AspireShowcase.BusinessSetup;

/// <summary>
/// Business Setup's own view of app-db: only its tables. Other modules get their own context,
/// so none of them can reach into another's tables.
/// </summary>
/// <remarks>
/// It keeps the default __EFMigrationsHistory table, because its migrations were the API's
/// before modules became projects, and existing databases have them recorded there. A new
/// module's context should use a history table of its own.
/// </remarks>
sealed class BusinessSetupDbContext(DbContextOptions<BusinessSetupDbContext> options) : DbContext(options)
{
    public DbSet<Business> Businesses => Set<Business>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BusinessSetupDbContext).Assembly);
}
