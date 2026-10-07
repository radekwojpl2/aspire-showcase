using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace AspireShowcase.Scheduling;

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
sealed class Bookings(SchedulingDbContext db, IPublishEndpoint publish)
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
            booking.Id.Value, booking.BusinessId.Value, confirmed.OccurredAt),
        _ => throw new InvalidOperationException($"No message for {bookingEvent.GetType().Name}."),
    };
}
