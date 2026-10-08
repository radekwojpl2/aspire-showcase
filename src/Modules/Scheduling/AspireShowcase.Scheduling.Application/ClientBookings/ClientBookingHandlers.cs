using System.Globalization;
using AspireShowcase.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;

namespace AspireShowcase.Scheduling.Application.ClientBookings;

// A client's own bookings (user story MVP-7): every business they booked with, and cancelling one,
// unless the business's cancellation policy says it's too late (V1-3).

/// <summary>The client's upcoming bookings: past and cancelled ones aren't listed.</summary>
sealed class ListMyBookings(ISchedulingDbContext db, IBusinessDirectory directory, TimeProvider time)
{
    public async Task<List<ClientBooking>> HandleAsync(string userId, CancellationToken cancellation)
    {
        var now = time.GetUtcNow();
        var bookings = await db.OfClient(userId)
            .Where(booking => booking.Status == BookingStatus.Confirmed && booking.Start > now)
            .OrderBy(booking => booking.Start)
            .AsNoTracking()
            .ToListAsync(cancellation);

        var list = new List<ClientBooking>();
        foreach (var business in bookings.GroupBy(booking => booking.BusinessId))
        {
            if (await directory.FindAsync(business.Key, cancellation) is not { } info)
            {
                continue;
            }
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(info.TimeZone);
            var services = (await directory.ServicesAsync(info.Id, cancellation)).ToDictionary(service => service.Id);
            var staff = (await directory.StaffAsync(info.Id, cancellation)).ToDictionary(member => member.Id);
            list.AddRange(business.Select(booking =>
            {
                var start = TimeZoneInfo.ConvertTime(booking.Start, timeZone);
                var end = TimeZoneInfo.ConvertTime(booking.End, timeZone);
                return new ClientBooking(
                    booking.Id.Value,
                    info.Name,
                    info.Slug,
                    services.TryGetValue(booking.ServiceId, out var service) ? service.Name : "A removed service",
                    staff.TryGetValue(booking.StaffMemberId, out var member) ? member.Name : "A former staff member",
                    start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    start.ToString("HH:mm", CultureInfo.InvariantCulture),
                    end.ToString("HH:mm", CultureInfo.InvariantCulture),
                    info.TimeZone,
                    booking.ClientCanChangeUntil,
                    info.ContactEmail);
            }));
        }
        return list.OrderBy(booking => booking.Date).ThenBy(booking => booking.Start).ToList();
    }
}

/// <summary>Frees the time at once; another client's booking is as good as missing.</summary>
sealed class CancelMyBooking(ISchedulingDbContext db, IBookings bookings, SchedulingTelemetry telemetry, TimeProvider time)
{
    public async Task<Result> HandleAsync(Guid id, string userId, CancellationToken cancellation)
    {
        using var activity = telemetry.StartActivity("bookings.cancel");

        var bookingId = new BookingId(id);
        if (await db.OfClient(userId).SingleOrDefaultAsync(booking => booking.Id == bookingId, cancellation) is not { } booking)
        {
            return Result.NotFound();
        }

        try
        {
            booking.Cancel(time.GetUtcNow(), CancelledBy.Client);
        }
        catch (DomainValidationException exception)
        {
            return Result.Conflict(exception.Errors.Values.First()[0]);
        }

        await bookings.SaveAsync(booking, cancellation);
        telemetry.Cancelled(activity, booking, "client");
        return Result.Done();
    }
}
