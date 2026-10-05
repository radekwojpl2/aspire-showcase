using System.Globalization;
using System.Security.Claims;
using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.Identity;
using Microsoft.EntityFrameworkCore;

namespace AspireShowcase.Scheduling;

/// <param name="Day">The local date, yyyy-MM-dd.</param>
/// <param name="Start">The local time, HH:mm, in the business's time zone.</param>
record CalendarBooking(
    Guid Id, string Day, string Start, string End, Guid StaffMemberId, string StaffName, string ServiceName,
    string ClientName, string ClientEmail);

record CalendarStaff(Guid Id, string Name);

/// <param name="Date">The date asked for; <paramref name="FirstDay"/> to <paramref name="LastDay"/> is what's shown.</param>
record CalendarResponse(
    string View, string Date, string FirstDay, string LastDay, string TimeZone, IReadOnlyList<CalendarStaff> Staff,
    IReadOnlyList<CalendarBooking> Bookings);

static class CalendarEndpoints
{
    /// <summary>
    /// The owner's calendar (user story MVP-12): bookings by day or week, for everyone or one staff
    /// member, in the business's time zone. Only the owner's own business, by the query filter.
    /// </summary>
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet("/businesses/mine/bookings", async (
            string? view, string? date, Guid? staffMemberId, ClaimsPrincipal user, IBusinessDirectory directory,
            SchedulingDbContext db, BusinessScope scope, SchedulingTelemetry telemetry, TimeProvider time,
            CancellationToken cancellation) =>
        {
            using var activity = telemetry.StartActivity("bookings.calendar");

            var ownerId = user.FindFirstValue("sub") ?? throw new InvalidOperationException("The access token has no sub claim.");
            if (await directory.FindOwnedAsync(ownerId, cancellation) is not { } business)
            {
                return Results.NotFound();
            }
            scope.BusinessId = business.Id;

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
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["calendar"] = ["Use view=day or view=week, and a date as yyyy-MM-dd."],
                });
            }

            var range = CalendarRange.For(day, calendarView.Value, timeZone);
            var query = db.Bookings.AsNoTracking()
                .Where(booking => booking.Status == BookingStatus.Confirmed &&
                                  booking.Start < range.To && booking.End > range.From);
            if (staffMemberId is { } staffId)
            {
                query = query.Where(booking => booking.StaffMemberId == staffId);
            }
            var bookings = await query.OrderBy(booking => booking.Start).ToListAsync(cancellation);

            var staff = await directory.StaffAsync(business.Id, cancellation);
            var services = (await directory.ServicesAsync(business.Id, cancellation)).ToDictionary(service => service.Id);
            var staffNames = staff.ToDictionary(member => member.Id, member => member.Name);

            telemetry.CalendarRead(activity, calendarView.Value, bookings.Count);
            return Results.Ok(new CalendarResponse(
                calendarView.Value.ToString().ToLowerInvariant(),
                Format(day),
                Format(range.FirstDay),
                Format(range.LastDay),
                business.TimeZone,
                staff.Select(member => new CalendarStaff(member.Id, member.Name)).ToList(),
                bookings.Select(booking =>
                {
                    var start = TimeZoneInfo.ConvertTime(booking.Start, timeZone);
                    var end = TimeZoneInfo.ConvertTime(booking.End, timeZone);
                    return new CalendarBooking(
                        booking.Id,
                        Format(DateOnly.FromDateTime(start.DateTime)),
                        start.ToString("HH:mm", CultureInfo.InvariantCulture),
                        end.ToString("HH:mm", CultureInfo.InvariantCulture),
                        booking.StaffMemberId,
                        staffNames.GetValueOrDefault(booking.StaffMemberId, "A former staff member"),
                        services.TryGetValue(booking.ServiceId, out var service) ? service.Name : "A removed service",
                        booking.Attendee.Name,
                        booking.Attendee.Email);
                }).ToList()));
        })
        .RequireAuthorization(IdentityAccess.OwnerPolicy)
        .WithName("GetCalendar");

    static string Format(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
