using Microsoft.EntityFrameworkCore;

namespace AspireShowcase.Scheduling;

/// <summary>Saves bookings, turning the database's overlap check into a domain result.</summary>
sealed class Bookings(SchedulingDbContext db)
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
            await db.SaveChangesAsync(cancellation);
            return BookingResult.Booked;
        }
        catch (DbUpdateException exception) when (BookingConfiguration.IsSlotTaken(exception))
        {
            db.Entry(booking).State = EntityState.Detached;
            return BookingResult.SlotTaken;
        }
    }
}
