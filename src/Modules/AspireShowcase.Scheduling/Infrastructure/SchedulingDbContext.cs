using Microsoft.EntityFrameworkCore;

namespace AspireShowcase.Scheduling;

/// <summary>
/// The business a request works for. Set once per request, before any query: Scheduling's
/// queries only ever see that business's bookings.
/// </summary>
sealed class BusinessScope
{
    public Guid? BusinessId { get; set; }
}

/// <summary>
/// Scheduling's own view of app-db: only its tables, in the scheduling schema, with its migration
/// history there too.
/// </summary>
/// <remarks>
/// Owner and staff queries are scoped to one business by a global query filter on BusinessId, so
/// a forgotten Where can't leak another business's bookings: with no business in scope, nothing
/// is found. "My bookings across all businesses" (MVP-7) crosses businesses on purpose; it will
/// get its own query path instead of switching the filter off.
/// </remarks>
sealed class SchedulingDbContext(DbContextOptions<SchedulingDbContext> options, BusinessScope scope) : DbContext(options)
{
    public const string Schema = "scheduling";

    public DbSet<Booking> Bookings => Set<Booking>();

    // A property of the context, so EF Core reads it anew for every query.
    Guid? ScopedBusinessId => scope.BusinessId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SchedulingDbContext).Assembly);
        modelBuilder.Entity<Booking>().HasQueryFilter(booking => booking.BusinessId == ScopedBusinessId);
    }
}
