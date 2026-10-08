using Microsoft.EntityFrameworkCore;

namespace AspireShowcase.Scheduling.Application;

/// <summary>
/// What the use cases need of Scheduling's storage; Infrastructure's DbContext provides it. Owner
/// and staff queries see only the business in <see cref="BusinessScope"/>.
/// </summary>
interface ISchedulingDbContext
{
    DbSet<Booking> Bookings { get; }

    DbSet<TimeOff> TimeOff { get; }

    DbSet<CancellationPolicy> CancellationPolicies { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The business a request works for. Set once per request, before any query: Scheduling's
/// queries only ever see that business's bookings, time off and policy.
/// </summary>
sealed class BusinessScope
{
    public BusinessId? BusinessId { get; set; }
}

/// <summary>
/// Saves bookings together with their events, which are published through the transactional
/// outbox; and turns the database's no-overlap check into <see cref="BookingResult.SlotTaken"/>.
/// </summary>
interface IBookings
{
    /// <summary>Adds the booking, unless another confirmed booking of the same staff member overlaps it.</summary>
    Task<BookingResult> AddAsync(Booking booking, CancellationToken cancellation);

    /// <summary>
    /// Books the first of <paramref name="staff"/> the database accepts ("anyone"): if it refuses one
    /// (someone booked them a moment ago), the next one is tried, each in a unit of work of its
    /// own, so a refused one leaves nothing behind.
    /// </summary>
    /// <param name="attempted">Told what came of each attempt.</param>
    /// <returns>The booking made, or null when the database refused them all.</returns>
    Task<Booking?> AddFirstAsync(
        BusinessId businessId, IEnumerable<StaffMemberId> staff, Func<StaffMemberId, Booking> book,
        Action<BookingResult> attempted, CancellationToken cancellation);

    /// <summary>Saves the booking's changes and publishes its events.</summary>
    Task SaveAsync(Booking booking, CancellationToken cancellation);

    /// <summary>Saves a booking moved to another time (V1-4), unless the new time overlaps another booking.</summary>
    Task<BookingResult> RescheduleAsync(Booking booking, CancellationToken cancellation);
}

static class Storage
{
    /// <summary>The business's policy, or none if it hasn't set one. The business must be in scope.</summary>
    public static async Task<CancellationPolicy> PolicyOfAsync(
        this ISchedulingDbContext db, BusinessId businessId, CancellationToken cancellation) =>
        await db.CancellationPolicies.SingleOrDefaultAsync(policy => policy.BusinessId == businessId, cancellation)
            ?? CancellationPolicy.None(businessId);

    /// <summary>
    /// The client's bookings at every business: the one query that crosses businesses on purpose.
    /// It switches the per-business query filter off here, and only here, and scopes the query to
    /// the client's own user ID instead.
    /// </summary>
    public static IQueryable<Booking> OfClient(this ISchedulingDbContext db, string userId) =>
        db.Bookings.IgnoreQueryFilters().Where(booking => booking.Attendee.UserId == userId);

    /// <summary>The signed-in owner's business, with the request scoped to it; null when they have none.</summary>
    public static async Task<BusinessInfo?> FindOwnedAsync(
        this IBusinessDirectory directory, string ownerId, BusinessScope scope, CancellationToken cancellation)
    {
        var business = await directory.FindOwnedAsync(ownerId, cancellation);
        scope.BusinessId = business?.Id;
        return business;
    }
}
