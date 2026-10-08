using AspireShowcase.BuildingBlocks.Infrastructure;
using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.Scheduling.Application;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace AspireShowcase.Scheduling;

/// <summary>
/// Scheduling's own view of app-db: only its tables, in the scheduling schema, with its migration
/// history there too.
/// </summary>
/// <remarks>
/// Owner and staff queries are scoped to one business by a global query filter on BusinessId, so
/// a forgotten Where can't leak another business's bookings or time off: with no business in scope, nothing
/// is found. "My bookings across all businesses" (MVP-7) crosses businesses on purpose; it will
/// get its own query path instead of switching the filter off.
/// </remarks>
sealed class SchedulingDbContext(DbContextOptions<SchedulingDbContext> options, BusinessScope scope)
    : DbContext(options), ISchedulingDbContext
{
    public const string Schema = "scheduling";

    public DbSet<Booking> Bookings => Set<Booking>();

    public DbSet<TimeOff> TimeOff => Set<TimeOff>();

    public DbSet<CancellationPolicy> CancellationPolicies => Set<CancellationPolicy>();


    // A property of the context, so EF Core reads it anew for every query.
    BusinessId? ScopedBusinessId => scope.BusinessId;

    // Typed IDs are stored as the uuid they wrap, wherever they appear.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<BookingId>().HaveConversion<TypedIdConverter<BookingId>>();
        configurationBuilder.Properties<BusinessId>().HaveConversion<TypedIdConverter<BusinessId>>();
        configurationBuilder.Properties<ServiceId>().HaveConversion<TypedIdConverter<ServiceId>>();
        configurationBuilder.Properties<StaffMemberId>().HaveConversion<TypedIdConverter<StaffMemberId>>();
        configurationBuilder.Properties<TimeOffId>().HaveConversion<TypedIdConverter<TimeOffId>>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SchedulingDbContext).Assembly);
        modelBuilder.Entity<Booking>().HasQueryFilter(booking => booking.BusinessId == ScopedBusinessId);
        modelBuilder.Entity<TimeOff>().HasQueryFilter(timeOff => timeOff.BusinessId == ScopedBusinessId);
        modelBuilder.Entity<CancellationPolicy>().HasQueryFilter(policy => policy.BusinessId == ScopedBusinessId);

        // MassTransit's transactional outbox and inbox: messages published while saving a booking
        // are written here in the same transaction and sent afterwards; messages consumed are
        // recorded, so a redelivered one isn't handled twice.
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
