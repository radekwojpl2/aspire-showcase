using System.Globalization;
using System.Security.Claims;
using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;

namespace AspireShowcase.Scheduling;

/// <param name="Date">The local date in the business's time zone, yyyy-MM-dd.</param>
/// <param name="Start">The local time, HH:mm.</param>
/// <param name="CanChangeUntil">Until when the client can cancel or move it themselves (V1-3).</param>
/// <param name="BusinessContactEmail">Where to reach the business after that; null if it has none yet.</param>
record ClientBooking(
    Guid Id, string BusinessName, string BusinessSlug, string ServiceName, string StaffName, string Date, string Start,
    string End, string TimeZone, DateTimeOffset CanChangeUntil, string? BusinessContactEmail);

/// <summary>
/// A client's own bookings (user story MVP-7), under /me/bookings: every business they booked
/// with, and cancelling one, unless the business's cancellation policy says it's too late (V1-3).
/// </summary>
static class ClientBookingEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var mine = api.MapGroup("/me/bookings").RequireAuthorization();

        // Upcoming only: past and cancelled bookings aren't listed.
        mine.MapGet("/", async (
            ClaimsPrincipal user, SchedulingDbContext db, IBusinessDirectory directory, TimeProvider time,
            CancellationToken cancellation) =>
        {
            var now = time.GetUtcNow();
            var bookings = await OfClient(db, UserId(user))
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
        })
        .WithName("GetMyBookings");

        // Frees the time at once; another client's booking answers 404, as if it didn't exist.
        mine.MapPost("/{id:guid}/cancel", async (
            Guid id, ClaimsPrincipal user, SchedulingDbContext db, Bookings bookings, SchedulingTelemetry telemetry,
            TimeProvider time, CancellationToken cancellation) =>
        {
            using var activity = telemetry.StartActivity("bookings.cancel");

            var bookingId = new BookingId(id);
            if (await OfClient(db, UserId(user)).SingleOrDefaultAsync(booking => booking.Id == bookingId, cancellation)
                is not { } booking)
            {
                return Results.NotFound();
            }

            try
            {
                booking.Cancel(time.GetUtcNow(), CancelledBy.Client);
            }
            catch (DomainValidationException exception)
            {
                return Results.Problem(
                    title: exception.Errors.Values.First()[0], statusCode: StatusCodes.Status409Conflict);
            }

            await bookings.SaveAsync(booking, cancellation);
            telemetry.Cancelled(activity, booking, "client");
            return Results.NoContent();
        })
        .WithName("CancelMyBooking");
    }

    /// <summary>
    /// The client's bookings at every business: the one query that crosses businesses on purpose.
    /// It switches the per-business query filter off here, and only here, and scopes the query to
    /// the client's own user ID instead.
    /// </summary>
    public static IQueryable<Booking> OfClient(SchedulingDbContext db, string userId) =>
        db.Bookings.IgnoreQueryFilters().Where(booking => booking.Attendee.UserId == userId);

    static string UserId(ClaimsPrincipal user) =>
        user.FindFirstValue("sub") ?? throw new InvalidOperationException("The access token has no sub claim.");
}
