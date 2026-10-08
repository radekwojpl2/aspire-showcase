using System.Globalization;
using AspireShowcase.BuildingBlocks.Domain;

namespace AspireShowcase.Scheduling.Application.PublicBooking;

// The public booking page (user stories MVP-1 to MVP-4): the business and its free slots for
// anyone, and booking for signed-in clients.

/// <summary>What a business offers, with who does it and its cancellation policy (MVP-1, MVP-2, V1-3).</summary>
sealed class GetPublicBusiness(IBusinessDirectory directory, ISchedulingDbContext db, BusinessScope scope)
{
    public async Task<Result<PublicBusiness>> HandleAsync(string slug, CancellationToken cancellation)
    {
        if (await directory.FindBySlugAsync(slug, cancellation) is not { } found)
        {
            return Result.NotFound();
        }

        var staff = await directory.StaffAsync(found.Id, cancellation);
        var services = (await directory.ServicesAsync(found.Id, cancellation))
            .Where(service => !service.IsHidden)
            .Select(service => new PublicService(
                service.Id.Value, service.Name, (int)service.Duration.TotalMinutes, service.Price, service.Currency,
                // MVP-2: only the staff who do the service are offered.
                staff.Where(member => member.DoesAllServices || member.ServiceIds.Contains(service.Id))
                    .Select(member => new PublicStaff(member.Id.Value, member.Name))
                    .ToList()))
            .ToList();
        // V1-3: clients see the policy before they book.
        scope.BusinessId = found.Id;
        var policy = await db.PolicyOfAsync(found.Id, cancellation);
        return new PublicBusiness(found.Name, found.Slug, found.TimeZone, (int)policy.Notice.TotalHours, services);
    }
}

/// <summary>Free slots for the next 4 weeks, for one staff member or anyone (MVP-1, MVP-2).</summary>
sealed class GetFreeSlots(IBusinessDirectory directory, Availability availability, SchedulingTelemetry telemetry)
{
    public async Task<Result<PublicSlots>> HandleAsync(
        string slug, Guid? serviceId, Guid? staffMemberId, CancellationToken cancellation)
    {
        using var activity = telemetry.StartActivity("availability.slots");

        if (serviceId is null ||
            await directory.FindBySlugAsync(slug, cancellation) is not { } found ||
            await availability.FindOfferAsync(found, new ServiceId(serviceId.Value), Slots.ToStaffId(staffMemberId), cancellation)
                is not { } offer)
        {
            return Result.NotFound();
        }

        var slots = await availability.FreeSlotsAsync(offer, availability.Today(offer), Availability.DaysAhead, cancellation);
        activity?.SetTag("availability.slots", slots.Count);
        return Slots.ToPublicSlots(slots, offer);
    }
}

/// <summary>Books a free slot for the signed-in client (MVP-4), whose account it's tied to (MVP-3).</summary>
sealed class BookSlotHandler(
    IBusinessDirectory directory, Availability availability, ISchedulingDbContext db, IBookings bookings,
    SchedulingTelemetry telemetry, TimeProvider time)
{
    public async Task<Result<BookingConfirmation>> HandleAsync(
        string slug, BookSlot request, string? userId, CancellationToken cancellation)
    {
        using var activity = telemetry.StartActivity("bookings.book");

        if (request.ServiceId is null ||
            await directory.FindBySlugAsync(slug, cancellation) is not { } found ||
            await availability.FindOfferAsync(
                found, new ServiceId(request.ServiceId.Value), Slots.ToStaffId(request.StaffMemberId), cancellation)
                is not { } offer)
        {
            return Result.NotFound();
        }

        Attendee attendee;
        try
        {
            attendee = Attendee.Create(userId, request.ClientName, request.ClientEmail);
        }
        catch (DomainValidationException exception)
        {
            return Result.Invalid(exception);
        }
        if (request.StartsAt is not { } startsAt)
        {
            return Result.Invalid("startsAt", "Choose a time.");
        }

        activity?.SetTag("service.buffer_minutes", (int)offer.Service.Buffer.TotalMinutes);

        // Only a slot that's free right now, on the grid and within someone's hours, can be booked.
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startsAt, offer.TimeZone).DateTime);
        var slot = (await availability.FreeSlotsAsync(offer, day, 1, cancellation))
            .SingleOrDefault(free => free.Start == startsAt.ToUniversalTime());
        // The booking keeps the policy the client saw (V1-3).
        var notice = (await db.PolicyOfAsync(found.Id, cancellation)).Notice;

        // "Anyone" takes whoever is free; if the database refuses one, the next one is tried.
        var booking = await bookings.AddFirstAsync(
            found.Id,
            slot?.FreeStaff ?? [],
            staffMemberId => Booking.Book(
                found.Id, staffMemberId, offer.Service.Id, startsAt, offer.Service.Duration, offer.Service.Buffer, notice,
                attendee, time.GetUtcNow()),
            result => telemetry.Booking(result, "client"),
            cancellation);
        if (booking is not null)
        {
            activity?.SetTag("booking.id", booking.Id.Value);
            return Slots.ToConfirmation(booking, offer, booking.StaffMemberId);
        }

        // Nobody to try at all is a taken slot too; refused attempts were counted already.
        if (slot is null || slot.FreeStaff.Count == 0)
        {
            telemetry.Booking(BookingResult.SlotTaken, "client");
        }
        return Result.Conflict(Slots.Taken);
    }
}

/// <summary>Free slots and booked times as the API gives them, in the business's time zone.</summary>
static class Slots
{
    public const string Taken = "This time was just taken.";

    public static StaffMemberId? ToStaffId(Guid? id) => id is { } value ? new StaffMemberId(value) : null;

    public static PublicSlots ToPublicSlots(IReadOnlyList<FreeSlot> slots, Offer offer) => new(
        offer.Business.TimeZone,
        slots
            .Select(slot => (Slot: slot, Local: TimeZoneInfo.ConvertTime(slot.Start, offer.TimeZone)))
            .GroupBy(slot => DateOnly.FromDateTime(slot.Local.DateTime))
            .Select(day => new PublicDay(
                day.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                day.Select(slot => new PublicSlot(slot.Local.ToString("HH:mm", CultureInfo.InvariantCulture), slot.Slot.Start))
                    .ToList()))
            .ToList());

    public static BookingConfirmation ToConfirmation(Booking booking, Offer offer, StaffMemberId staffMemberId)
    {
        var start = TimeZoneInfo.ConvertTime(booking.Start, offer.TimeZone);
        var end = TimeZoneInfo.ConvertTime(booking.End, offer.TimeZone);
        return new BookingConfirmation(
            booking.Id.Value,
            offer.Service.Name,
            offer.Staff.Single(member => member.Id == staffMemberId).Name,
            start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            start.ToString("HH:mm", CultureInfo.InvariantCulture),
            end.ToString("HH:mm", CultureInfo.InvariantCulture));
    }
}
