using System.Globalization;
using AspireShowcase.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;

namespace AspireShowcase.Scheduling.Application.TimeOffs;

// The owner's time off (user story V1-1): holidays and breaks of the business or one staff
// member. Nobody can book them; the bookings already in them are listed, for the owner to cancel
// or move. Only the owner's own business, by the query filter.

/// <summary>What hasn't ended yet, with the bookings still in it.</summary>
sealed class ListTimeOff(IBusinessDirectory directory, ISchedulingDbContext db, BusinessScope scope, TimeProvider time)
{
    public async Task<Result<TimeOffList>> HandleAsync(string ownerId, CancellationToken cancellation)
    {
        if (await directory.FindOwnedAsync(ownerId, scope, cancellation) is not { } business)
        {
            return Result.NotFound();
        }

        var now = time.GetUtcNow();
        var blocks = await db.TimeOff.AsNoTracking()
            .Where(t => t.End > now)
            .OrderBy(t => t.Start)
            .ToListAsync(cancellation);
        var details = await TimeOffDetails.LoadAsync(business, blocks, directory, db, cancellation);
        return new TimeOffList(business.TimeZone, blocks.Select(details.ToResponse).ToList());
    }
}

/// <summary>Blocks a time for the business or one staff member, and lists the bookings already in it.</summary>
sealed class AddTimeOff(
    IBusinessDirectory directory, ISchedulingDbContext db, BusinessScope scope, SchedulingTelemetry telemetry,
    TimeProvider time)
{
    public async Task<Result<TimeOffResponse>> HandleAsync(TimeOffBody request, string ownerId, CancellationToken cancellation)
    {
        using var activity = telemetry.StartActivity("time_off.add");

        if (await directory.FindOwnedAsync(ownerId, scope, cancellation) is not { } business)
        {
            return Result.NotFound();
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
            return Result.Invalid(errors);
        }

        TimeOff block;
        try
        {
            block = TimeOff.Block(business.Id, staffMemberId, start!.Value, end!.Value, request.Note, time.GetUtcNow());
        }
        catch (DomainValidationException exception)
        {
            telemetry.TimeOffChanged(activity, null, "invalid");
            return Result.Invalid(exception);
        }

        db.TimeOff.Add(block);
        await db.SaveChangesAsync(cancellation);

        var response = (await TimeOffDetails.LoadAsync(business, [block], directory, db, cancellation)).ToResponse(block);
        telemetry.TimeOffChanged(activity, block, "added", response.Bookings.Count);
        return response;
    }

    static DateTimeOffset? ParseLocal(string? value, string field, TimeZoneInfo timeZone, Dictionary<string, string[]> errors)
    {
        if (!DateTime.TryParseExact(value, TimeOffDetails.LocalFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
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
}

/// <summary>Removes a time off; another business's is as good as missing.</summary>
sealed class RemoveTimeOff(
    IBusinessDirectory directory, ISchedulingDbContext db, BusinessScope scope, SchedulingTelemetry telemetry)
{
    public async Task<Result> HandleAsync(Guid id, string ownerId, CancellationToken cancellation)
    {
        using var activity = telemetry.StartActivity("time_off.remove");

        if (await directory.FindOwnedAsync(ownerId, scope, cancellation) is null)
        {
            return Result.NotFound();
        }

        var timeOffId = new TimeOffId(id);
        if (await db.TimeOff.SingleOrDefaultAsync(t => t.Id == timeOffId, cancellation) is not { } block)
        {
            return Result.NotFound();
        }

        db.TimeOff.Remove(block);
        await db.SaveChangesAsync(cancellation);
        telemetry.TimeOffChanged(activity, block, "removed");
        return Result.Done();
    }
}

/// <summary>What the responses add to time off: names, local times, and the bookings inside it.</summary>
sealed class TimeOffDetails(
    TimeZoneInfo timeZone, Dictionary<StaffMemberId, string> staffNames, Dictionary<ServiceId, string> serviceNames,
    List<Booking> bookings)
{
    public const string LocalFormat = "yyyy-MM-dd'T'HH:mm";

    public static async Task<TimeOffDetails> LoadAsync(
        BusinessInfo business, IReadOnlyList<TimeOff> blocks, IBusinessDirectory directory, ISchedulingDbContext db,
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
        return new TimeOffDetails(
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
