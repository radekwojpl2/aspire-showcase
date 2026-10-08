using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.Scheduling.Application;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace AspireShowcase.Scheduling.Infrastructure;

/// <summary>
/// Saves bookings and publishes what happened to them, together: the events a booking recorded go
/// to MassTransit's transactional outbox in the same database transaction, so a booking is never
/// saved without its news, nor news sent about a booking that wasn't saved. Also turns the
/// database's overlap check into a domain result.
/// </summary>
/// <remarks>
/// After a refused save the outbox messages stay in the request's DbContext, so a second attempt
/// needs a scope of its own (as booking "anyone" does): see PublicBookingEndpoints.
/// </remarks>
sealed class Bookings(SchedulingDbContext db, IPublishEndpoint publish, IServiceScopeFactory scopes) : IBookings
{
    /// <summary>
    /// Adds the booking, unless another confirmed booking of the same staff member overlaps it.
    /// That's decided by the exclusion constraint, so two clients booking the same slot at the
    /// same moment can't both get it.
    /// </summary>
    public async Task<BookingResult> AddAsync(Booking booking, CancellationToken cancellation)
    {
        db.Bookings.Add(booking);
        try
        {
            await SaveAsync(booking, cancellation);
            return BookingResult.Booked;
        }
        catch (DbUpdateException exception) when (BookingConfiguration.IsSlotTaken(exception))
        {
            db.Entry(booking).State = EntityState.Detached;
            return BookingResult.SlotTaken;
        }
    }

    /// <summary>
    /// Books the first staff member the database accepts. Each attempt gets a scope of its own: after
    /// a refused save, the outbox messages stay in that scope's DbContext, so they can't go out
    /// with the next attempt.
    /// </summary>
    public async Task<Booking?> AddFirstAsync(
        BusinessId businessId, IEnumerable<StaffMemberId> staff, Func<StaffMemberId, Booking> book,
        Action<BookingResult> attempted, CancellationToken cancellation)
    {
        foreach (var staffMemberId in staff)
        {
            await using var attempt = scopes.CreateAsyncScope();
            attempt.ServiceProvider.GetRequiredService<BusinessScope>().BusinessId = businessId;
            var booking = book(staffMemberId);
            var result = await attempt.ServiceProvider.GetRequiredService<Bookings>().AddAsync(booking, cancellation);
            attempted(result);
            if (result == BookingResult.Booked)
            {
                return booking;
            }
        }
        return null;
    }

    /// <summary>
    /// Saves a booking moved to another time (V1-4), unless another confirmed booking of that
    /// staff member overlaps the new time; as in <see cref="AddAsync"/>, the database decides.
    /// </summary>
    public async Task<BookingResult> RescheduleAsync(Booking booking, CancellationToken cancellation)
    {
        try
        {
            await SaveAsync(booking, cancellation);
            return BookingResult.Booked;
        }
        catch (DbUpdateException exception) when (BookingConfiguration.IsSlotTaken(exception))
        {
            db.Entry(booking).State = EntityState.Detached;
            return BookingResult.SlotTaken;
        }
    }

    /// <summary>Saves the booking's changes and publishes its events through the outbox.</summary>
    public async Task SaveAsync(Booking booking, CancellationToken cancellation)
    {
        foreach (var bookingEvent in booking.Events)
        {
            await publish.Publish(ToMessage(booking, bookingEvent), cancellation);
        }
        await db.SaveChangesAsync(cancellation);
        booking.ClearEvents();
    }

    static object ToMessage(Booking booking, BookingEvent bookingEvent) => bookingEvent switch
    {
        BookingCancelled cancelled => new PublicClient.BookingCancelled(
            booking.Id.Value, booking.BusinessId.Value, cancelled.By.ToString().ToLowerInvariant(), cancelled.OccurredAt),
        BookingRescheduled rescheduled => new PublicClient.BookingRescheduled(
            booking.Id.Value, booking.BusinessId.Value, rescheduled.By.ToString().ToLowerInvariant(),
            rescheduled.PreviousStart, rescheduled.OccurredAt),
        BookingConfirmed confirmed => new PublicClient.BookingConfirmed(
            booking.Id.Value, booking.BusinessId.Value, confirmed.OccurredAt, confirmed.By.ToString().ToLowerInvariant()),
        _ => throw new InvalidOperationException($"No message for {bookingEvent.GetType().Name}."),
    };
}
