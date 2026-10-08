using System.Globalization;
using AspireShowcase.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;

namespace AspireShowcase.Scheduling.Application.Calendar;

// The owner's calendar (user story MVP-12) and cancelling a booking for a client (MVP-14). Only
// the owner's own business, by the query filter.

/// <summary>
/// Bookings and time off (V1-1) by day or week, for everyone or one staff member, in the
/// business's time zone.
/// </summary>
sealed class GetCalendar(
    IBusinessDirectory directory, ISchedulingDbContext db, BusinessScope scope, SchedulingTelemetry telemetry,
    TimeProvider time)
{
    public async Task<Result<CalendarResponse>> HandleAsync(
        string? view, string? date, Guid? staffMemberId, string ownerId, CancellationToken cancellation)
    {
        using var activity = telemetry.StartActivity("bookings.calendar");

        if (await directory.FindOwnedAsync(ownerId, scope, cancellation) is not { } business)
        {
            return Result.NotFound();
        }

        var calendarView = view?.ToLowerInvariant() switch
        {
            null or "week" => CalendarView.Week,
            "day" => CalendarView.Day,
            _ => (CalendarView?)null,
        };
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), timeZone).DateTime);
        DateOnly day = today;
        if (calendarView is null || (date is not null &&
            !DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)))
        {
            return Result.Invalid("calendar", "Use view=day or view=week, and a date as yyyy-MM-dd.");
        }

        var range = CalendarRange.For(day, calendarView.Value, timeZone);
        var query = db.Bookings.AsNoTracking()
            .Where(booking => booking.Status == BookingStatus.Confirmed &&
                              booking.Start < range.To && booking.End > range.From);
        if (staffMemberId is { } id)
        {
            var staffId = new StaffMemberId(id);
            query = query.Where(booking => booking.StaffMemberId == staffId);
        }
        var bookings = await query.OrderBy(booking => booking.Start).ToListAsync(cancellation);

        // The whole business's time off, and the staff member's own when the calendar is theirs.
        var blocks = await db.TimeOff.AsNoTracking()
            .Where(t => t.Start < range.To && t.End > range.From)
            .OrderBy(t => t.Start)
            .ToListAsync(cancellation);
        if (staffMemberId is { } shownId)
        {
            blocks = blocks.Where(t => t.Covers(new StaffMemberId(shownId))).ToList();
        }

        var staff = await directory.StaffAsync(business.Id, cancellation);
        var services = (await directory.ServicesAsync(business.Id, cancellation)).ToDictionary(service => service.Id);
        var staffNames = staff.ToDictionary(member => member.Id, member => member.Name);

        telemetry.CalendarRead(activity, calendarView.Value, bookings.Count);
        return new CalendarResponse(
            calendarView.Value.ToString().ToLowerInvariant(),
            Format(day),
            Format(range.FirstDay),
            Format(range.LastDay),
            business.TimeZone,
            staff.Select(member => new CalendarStaff(member.Id.Value, member.Name)).ToList(),
            bookings.Select(booking =>
            {
                var start = TimeZoneInfo.ConvertTime(booking.Start, timeZone);
                var end = TimeZoneInfo.ConvertTime(booking.End, timeZone);
                return new CalendarBooking(
                    booking.Id.Value,
                    Format(DateOnly.FromDateTime(start.DateTime)),
                    start.ToString("HH:mm", CultureInfo.InvariantCulture),
                    end.ToString("HH:mm", CultureInfo.InvariantCulture),
                    booking.StaffMemberId.Value,
                    staffNames.GetValueOrDefault(booking.StaffMemberId, "A former staff member"),
                    services.TryGetValue(booking.ServiceId, out var service) ? service.Name : "A removed service",
                    booking.Attendee.Name,
                    booking.Attendee.Email);
            }).ToList(),
            TimeOffByDay(blocks, range, timeZone, staffNames));
    }

    // Each time off, cut into the days it covers: a week's holiday shows on every day of it.
    static List<CalendarTimeOff> TimeOffByDay(
        List<TimeOff> blocks, CalendarRange range, TimeZoneInfo timeZone, Dictionary<StaffMemberId, string> staffNames)
    {
        List<CalendarTimeOff> pieces = [];
        for (var day = range.FirstDay; day <= range.LastDay; day = day.AddDays(1))
        {
            var dayRange = CalendarRange.For(day, CalendarView.Day, timeZone);
            foreach (var block in blocks.Where(t => t.Overlaps(dayRange.From, dayRange.To)))
            {
                var start = block.Start > dayRange.From ? block.Start : dayRange.From;
                pieces.Add(new CalendarTimeOff(
                    block.Id.Value,
                    Format(day),
                    TimeZoneInfo.ConvertTime(start, timeZone).ToString("HH:mm", CultureInfo.InvariantCulture),
                    block.End >= dayRange.To
                        ? "24:00"
                        : TimeZoneInfo.ConvertTime(block.End, timeZone).ToString("HH:mm", CultureInfo.InvariantCulture),
                    block.StaffMemberId?.Value,
                    block.StaffMemberId is { } id ? staffNames.GetValueOrDefault(id, "A former staff member") : null,
                    block.Note));
            }
        }
        return pieces;
    }

    static string Format(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>
/// Cancels a booking for its client, for sickness or emergencies (MVP-14); the time is free again
/// at once. Another business's booking is as good as missing.
/// </summary>
sealed class CancelBooking(
    IBusinessDirectory directory, ISchedulingDbContext db, BusinessScope scope, IBookings bookings,
    SchedulingTelemetry telemetry, TimeProvider time)
{
    public async Task<Result> HandleAsync(Guid id, string ownerId, CancellationToken cancellation)
    {
        using var activity = telemetry.StartActivity("bookings.cancel");

        if (await directory.FindOwnedAsync(ownerId, scope, cancellation) is null)
        {
            return Result.NotFound();
        }

        var bookingId = new BookingId(id);
        if (await db.Bookings.SingleOrDefaultAsync(booking => booking.Id == bookingId, cancellation) is not { } booking)
        {
            return Result.NotFound();
        }

        try
        {
            booking.Cancel(time.GetUtcNow(), CancelledBy.Business);
        }
        catch (DomainValidationException exception)
        {
            return Result.Conflict(exception.Errors.Values.First()[0]);
        }

        await bookings.SaveAsync(booking, cancellation);
        telemetry.Cancelled(activity, booking, "business");
        return Result.Done();
    }
}
