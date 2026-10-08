using System.Globalization;
using AspireShowcase.Identity.PublicClient;
using AspireShowcase.Scheduling.PublicClient;
using Microsoft.EntityFrameworkCore;

namespace AspireShowcase.Scheduling.Application.Notices;

/// <summary>
/// Turns a booking event into a <see cref="BookingNotice"/>: looks up the business, service and
/// staff names, the times in the business's time zone, and the owner's email (from Logto, through
/// Identity). The notifications service turns the notice into emails without asking anyone anything.
/// </summary>
/// <remarks>
/// Run by the bus consumer, out of the booking request on purpose: booking stays quick and doesn't
/// fail when Logto is slow or down. If a lookup fails, the message is retried.
/// </remarks>
sealed class BuildBookingNotice(ISchedulingDbContext db, IBusinessDirectory directory, IUserProfiles profiles)
{
    /// <param name="kind">confirmed or cancelled.</param>
    /// <param name="cancelledBy">client or business, for a cancellation.</param>
    /// <returns>Null when there's nobody to tell any more, such as a booking of a business that's gone.</returns>
    public async Task<BookingNotice?> HandleAsync(Guid id, string kind, string? cancelledBy, CancellationToken cancellation)
    {
        // Not tied to one request's business: messages come for every business.
        var bookingId = new BookingId(id);
        var booking = await db.Bookings.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(b => b.Id == bookingId, cancellation);
        if (booking is null || await directory.FindAsync(booking.BusinessId, cancellation) is not { } business)
        {
            return null;
        }

        var service = (await directory.ServicesAsync(business.Id, cancellation)).SingleOrDefault(s => s.Id == booking.ServiceId);
        var staffMember = (await directory.StaffAsync(business.Id, cancellation)).SingleOrDefault(s => s.Id == booking.StaffMemberId);
        var owner = await profiles.FindAsync(business.OwnerId, cancellation);

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZone);
        var start = TimeZoneInfo.ConvertTime(booking.Start, timeZone);
        var end = TimeZoneInfo.ConvertTime(booking.End, timeZone);
        return new BookingNotice(
            kind,
            cancelledBy,
            booking.Id.Value,
            business.Name,
            business.Slug,
            service?.Name ?? "A removed service",
            staffMember?.Name ?? "A former staff member",
            start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            start.ToString("HH:mm", CultureInfo.InvariantCulture),
            end.ToString("HH:mm", CultureInfo.InvariantCulture),
            business.TimeZone,
            new BookingParty(booking.Attendee.Name, booking.Attendee.Email),
            new BookingParty(owner?.Name ?? business.Name, owner?.Email));
    }

    /// <summary>For a booking moved to another time (V1-4): where it was, as well as where it is.</summary>
    /// <param name="rescheduledBy">client or business.</param>
    public async Task<BookingNotice?> RescheduledAsync(
        Guid id, string rescheduledBy, DateTimeOffset previousStart, CancellationToken cancellation)
    {
        if (await HandleAsync(id, "rescheduled", null, cancellation) is not { } notice)
        {
            return null;
        }
        var before = TimeZoneInfo.ConvertTime(previousStart, TimeZoneInfo.FindSystemTimeZoneById(notice.TimeZone));
        return notice with
        {
            RescheduledBy = rescheduledBy,
            PreviousDate = before.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PreviousStart = before.ToString("HH:mm", CultureInfo.InvariantCulture),
        };
    }
}
