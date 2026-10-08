using AspireShowcase.BusinessSetup.PublicClient;
using Microsoft.EntityFrameworkCore;

namespace AspireShowcase.Scheduling.Application;

/// <summary>What can be booked: a business's service, and the staff who do it.</summary>
/// <param name="Staff">Everyone who does the service, or the one staff member asked for.</param>
sealed record Offer(BusinessInfo Business, ServiceInfo Service, IReadOnlyList<StaffInfo> Staff, TimeZoneInfo TimeZone);

/// <summary>
/// Gathers what <see cref="AvailabilityCalculator"/> needs: hours from Business Setup, through its
/// public client, and bookings and time off from Scheduling's own tables.
/// </summary>
/// <remarks>
/// Asks Business Setup on every request for now. ddd-modules.md has Scheduling keep its own copy
/// of staff hours and services, built from Business Setup's events, because this is the hot path;
/// that comes with the event plumbing.
/// </remarks>
sealed class Availability(IBusinessDirectory directory, ISchedulingDbContext db, BusinessScope scope, TimeProvider time)
{
    /// <summary>How far ahead clients can book (MVP-1: the next 4 weeks).</summary>
    public const int DaysAhead = 28;

    /// <summary>
    /// The service, if the business offers it to clients, with the staff who do it: everyone, or
    /// only <paramref name="staffMemberId"/> when given. Null when there's nothing to book.
    /// </summary>
    public async Task<Offer?> FindOfferAsync(
        BusinessInfo business, ServiceId serviceId, StaffMemberId? staffMemberId, CancellationToken cancellation)
    {
        var service = (await directory.ServicesAsync(business.Id, cancellation))
            .SingleOrDefault(service => service.Id == serviceId && !service.IsHidden);
        if (service is null)
        {
            return null;
        }

        var staff = (await directory.StaffAsync(business.Id, cancellation))
            .Where(member => member.DoesAllServices || member.ServiceIds.Contains(serviceId))
            .Where(member => staffMemberId is null || member.Id == staffMemberId)
            .ToList();
        return staff.Count == 0
            ? null
            : new Offer(business, service, staff, TimeZoneInfo.FindSystemTimeZoneById(business.TimeZone));
    }

    /// <summary>The business's local date today.</summary>
    public DateOnly Today(Offer offer) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), offer.TimeZone).DateTime);

    /// <summary>Free slots for the offer on <paramref name="days"/> local days from <paramref name="firstDay"/>.</summary>
    /// <param name="moving">
    /// A booking being moved to another time (V1-4): its own time doesn't count as busy, and the
    /// slots fit its length and buffer, which it keeps from when it was booked.
    /// </param>
    public async Task<IReadOnlyList<FreeSlot>> FreeSlotsAsync(
        Offer offer, DateOnly firstDay, int days, CancellationToken cancellation, Booking? moving = null)
    {
        scope.BusinessId = offer.Business.Id;

        // A day either side, so bookings that cross local midnight are seen.
        var from = firstDay.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = firstDay.AddDays(days + 1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var staffIds = offer.Staff.Select(member => member.Id).ToList();
        var bookings = db.Bookings.AsNoTracking()
            .Where(booking => booking.Status == BookingStatus.Confirmed &&
                              staffIds.Contains(booking.StaffMemberId) &&
                              booking.Start < new DateTimeOffset(to) && booking.OccupiedUntil > new DateTimeOffset(from));
        if (moving is not null)
        {
            bookings = bookings.Where(booking => booking.Id != moving.Id);
        }
        var busy = await bookings
            .Select(booking => new BusyTime(booking.StaffMemberId, booking.Start, booking.OccupiedUntil))
            .ToListAsync(cancellation);

        // Time off (V1-1) is busy time too, for everyone it covers.
        var timeOff = await db.TimeOff.AsNoTracking()
            .Where(t => t.Start < new DateTimeOffset(to) && t.End > new DateTimeOffset(from))
            .ToListAsync(cancellation);
        busy.AddRange(
            from t in timeOff
            from member in offer.Staff
            where t.Covers(member.Id)
            select new BusyTime(member.Id, t.Start, t.End));

        return AvailabilityCalculator.FreeSlots(
            offer.Staff.Select(member => new StaffSchedule(member.Id, member.WorkingHours)),
            moving is null ? offer.Service.Duration : moving.End - moving.Start,
            moving is null ? offer.Service.Buffer : moving.OccupiedUntil - moving.End,
            busy, offer.TimeZone, firstDay, days, time.GetUtcNow());
    }
}
