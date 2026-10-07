using System.Globalization;
using System.Security.Claims;
using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.Identity;
using AspireShowcase.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;

namespace AspireShowcase.Scheduling;

/// <param name="StaffMemberId">Who's away, or null for the whole business.</param>
/// <param name="From">The local start, yyyy-MM-ddTHH:mm, in the business's time zone.</param>
/// <param name="To">The local end, the same way; midnight for a whole last day.</param>
record TimeOffBody(Guid? StaffMemberId, string? From, string? To, string? Note);

/// <param name="Bookings">The confirmed bookings inside it, which the owner may want to cancel.</param>
record TimeOffResponse(
    Guid Id, Guid? StaffMemberId, string? StaffName, string From, string To, string? Note,
    IReadOnlyList<TimeOffBooking> Bookings);

/// <param name="Day">The local date, yyyy-MM-dd.</param>
/// <param name="Start">The local time, HH:mm.</param>
record TimeOffBooking(
    Guid Id, string Day, string Start, string End, string StaffName, string ServiceName, string ClientName);

record TimeOffList(string TimeZone, IReadOnlyList<TimeOffResponse> TimeOff);

/// <summary>
/// The owner's time off (user story V1-1), under /businesses/mine/time-off: holidays and breaks
/// of the business or one staff member. Nobody can book them; the bookings already in them are
/// listed, for the owner to cancel. Only the owner's own business, by the query filter.
/// </summary>
static class TimeOffEndpoints
{
    const string LocalFormat = "yyyy-MM-dd'T'HH:mm";

    public static void Map(IEndpointRouteBuilder api)
    {
        var timeOff = api.MapGroup("/businesses/mine/time-off").RequireAuthorization(IdentityAccess.OwnerPolicy);

        // What hasn't ended yet, with the bookings still in it.
        timeOff.MapGet("/", async (
            ClaimsPrincipal user, IBusinessDirectory directory, SchedulingDbContext db, BusinessScope scope,
            TimeProvider time, CancellationToken cancellation) =>
        {
            if (await FindBusinessAsync(user, directory, scope, cancellation) is not { } business)
            {
                return Results.NotFound();
            }

            var now = time.GetUtcNow();
            var blocks = await db.TimeOff.AsNoTracking()
                .Where(t => t.End > now)
                .OrderBy(t => t.Start)
                .ToListAsync(cancellation);
            var details = await Details.LoadAsync(business, blocks, directory, db, cancellation);
            return Results.Ok(new TimeOffList(business.TimeZone, blocks.Select(details.ToResponse).ToList()));
        })
        .WithName("GetTimeOff");

        timeOff.MapPost("/", async (
            TimeOffBody request, ClaimsPrincipal user, IBusinessDirectory directory, SchedulingDbContext db,
            BusinessScope scope, SchedulingTelemetry telemetry, TimeProvider time, CancellationToken cancellation) =>
        {
            using var activity = telemetry.StartActivity("time_off.add");

            if (await FindBusinessAsync(user, directory, scope, cancellation) is not { } business)
            {
                return Results.NotFound();
            }

            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZone);
            var errors = new Dictionary<string, string[]>();
            var start = ParseLocal(request.From, "from", timeZone, errors);
            var end = ParseLocal(request.To, "to", timeZone, errors);
            StaffMemberId? staffMemberId = request.StaffMemberId is { } id ? new StaffMemberId(id) : null;
            if (staffMemberId is { } staffId &&
                (await directory.StaffAsync(business.Id, cancellation)).All(member => member.Id != staffId))
            {
                errors["staffMemberId"] = ["Choose someone on your staff."];
            }
            if (errors.Count > 0)
            {
                telemetry.TimeOffChanged(activity, null, "invalid");
                return Results.ValidationProblem(errors);
            }

            TimeOff block;
            try
            {
                block = TimeOff.Block(business.Id, staffMemberId, start!.Value, end!.Value, request.Note, time.GetUtcNow());
            }
            catch (DomainValidationException exception)
            {
                telemetry.TimeOffChanged(activity, null, "invalid");
                return Results.ValidationProblem(exception.Errors.ToDictionary());
            }

            db.TimeOff.Add(block);
            await db.SaveChangesAsync(cancellation);

            var response = (await Details.LoadAsync(business, [block], directory, db, cancellation)).ToResponse(block);
            telemetry.TimeOffChanged(activity, block, "added", response.Bookings.Count);
            return Results.Created($"/api/businesses/mine/time-off/{block.Id}", response);
        })
        .WithName("AddTimeOff");

        timeOff.MapDelete("/{id:guid}", async (
            Guid id, ClaimsPrincipal user, IBusinessDirectory directory, SchedulingDbContext db, BusinessScope scope,
            SchedulingTelemetry telemetry, CancellationToken cancellation) =>
        {
            using var activity = telemetry.StartActivity("time_off.remove");

            if (await FindBusinessAsync(user, directory, scope, cancellation) is null)
            {
                return Results.NotFound();
            }

            // Another business's time off answers 404: the query filter doesn't see it.
            var timeOffId = new TimeOffId(id);
            if (await db.TimeOff.SingleOrDefaultAsync(t => t.Id == timeOffId, cancellation) is not { } block)
            {
                return Results.NotFound();
            }

            db.TimeOff.Remove(block);
            await db.SaveChangesAsync(cancellation);
            telemetry.TimeOffChanged(activity, block, "removed");
            return Results.NoContent();
        })
        .WithName("RemoveTimeOff");
    }

    // The signed-in owner's business, with the request scoped to it; null when they have none.
    static async Task<BusinessInfo?> FindBusinessAsync(
        ClaimsPrincipal user, IBusinessDirectory directory, BusinessScope scope, CancellationToken cancellation)
    {
        var ownerId = user.FindFirstValue("sub") ?? throw new InvalidOperationException("The access token has no sub claim.");
        var business = await directory.FindOwnedAsync(ownerId, cancellation);
        scope.BusinessId = business?.Id;
        return business;
    }

    static DateTimeOffset? ParseLocal(string? value, string field, TimeZoneInfo timeZone, Dictionary<string, string[]> errors)
    {
        if (!DateTime.TryParseExact(value, LocalFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            errors[field] = ["Give a date and time as yyyy-MM-ddTHH:mm."];
            return null;
        }
        if (AvailabilityCalculator.Instant(DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local), timeZone) is not { } instant)
        {
            errors[field] = ["The clocks skip this time; choose another."];
            return null;
        }
        return instant;
    }

    /// <summary>What the responses add to time off: names, local times, and the bookings inside it.</summary>
    sealed class Details(
        TimeZoneInfo timeZone, Dictionary<StaffMemberId, string> staffNames, Dictionary<ServiceId, string> serviceNames,
        List<Booking> bookings)
    {
        public static async Task<Details> LoadAsync(
            BusinessInfo business, IReadOnlyList<TimeOff> blocks, IBusinessDirectory directory, SchedulingDbContext db,
            CancellationToken cancellation)
        {
            var staff = await directory.StaffAsync(business.Id, cancellation);
            var services = await directory.ServicesAsync(business.Id, cancellation);
            List<Booking> bookings = [];
            if (blocks.Count > 0)
            {
                var from = blocks.Min(t => t.Start);
                var to = blocks.Max(t => t.End);
                bookings = await db.Bookings.AsNoTracking()
                    .Where(booking => booking.Status == BookingStatus.Confirmed && booking.Start < to && booking.End > from)
                    .OrderBy(booking => booking.Start)
                    .ToListAsync(cancellation);
            }
            return new Details(
                TimeZoneInfo.FindSystemTimeZoneById(business.TimeZone),
                staff.ToDictionary(member => member.Id, member => member.Name),
                services.ToDictionary(service => service.Id, service => service.Name),
                bookings);
        }

        public TimeOffResponse ToResponse(TimeOff block) => new(
            block.Id.Value,
            block.StaffMemberId?.Value,
            block.StaffMemberId is { } id ? StaffName(id) : null,
            Local(block.Start).ToString(LocalFormat, CultureInfo.InvariantCulture),
            Local(block.End).ToString(LocalFormat, CultureInfo.InvariantCulture),
            block.Note,
            bookings
                .Where(booking => block.Covers(booking.StaffMemberId) && block.Overlaps(booking.Start, booking.End))
                .Select(booking => new TimeOffBooking(
                    booking.Id.Value,
                    Local(booking.Start).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Local(booking.Start).ToString("HH:mm", CultureInfo.InvariantCulture),
                    Local(booking.End).ToString("HH:mm", CultureInfo.InvariantCulture),
                    StaffName(booking.StaffMemberId),
                    serviceNames.GetValueOrDefault(booking.ServiceId, "A removed service"),
                    booking.Attendee.Name))
                .ToList());

        DateTimeOffset Local(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, timeZone);

        string StaffName(StaffMemberId id) => staffNames.GetValueOrDefault(id, "A former staff member");
    }
}
