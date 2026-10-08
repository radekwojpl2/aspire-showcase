using AspireShowcase.BuildingBlocks.Domain;
using AspireShowcase.Scheduling.Application.PublicBooking;
using Microsoft.EntityFrameworkCore;

namespace AspireShowcase.Scheduling.Application.Rescheduling;

// Moving a booking to another time (user story V1-4): by the client, within the cancellation
// policy (V1-3); or by the owner, at any time, such as for bookings inside time off (V1-1). Each
// side gets the free slots the booking can move to, then moves it to one of them.

/// <summary>The free times the client's own booking can move to.</summary>
sealed class GetMyBookingSlots(ISchedulingDbContext db, Moves moves)
{
    public async Task<Result<PublicSlots>> HandleAsync(
        Guid id, Guid? staffMemberId, string userId, CancellationToken cancellation) =>
        await Moves.ClientBookingAsync(db, userId, id, cancellation) is { } booking
            ? await moves.SlotsAsync(booking, staffMemberId, cancellation)
            : Result.NotFound();
}

/// <summary>Moves the client's own booking, unless the cancellation policy says it's too late.</summary>
sealed class RescheduleMyBooking(ISchedulingDbContext db, Moves moves, SchedulingTelemetry telemetry, TimeProvider time)
{
    const string TooLate = "It's too late to move it online: contact the business.";

    public async Task<Result<BookingConfirmation>> HandleAsync(
        Guid id, RescheduleBody request, string userId, CancellationToken cancellation)
    {
        using var activity = telemetry.StartActivity("bookings.reschedule");

        if (await Moves.ClientBookingAsync(db, userId, id, cancellation) is not { } booking)
        {
            return Result.NotFound();
        }
        // Before looking for the new time: inside the notice, no time would do.
        if (!booking.ClientCanChange(time.GetUtcNow()))
        {
            telemetry.Rescheduled(activity, booking, "too_late", "client");
            return Result.Conflict(TooLate);
        }
        return await moves.MoveAsync(booking, request, RescheduledBy.Client, activity, cancellation);
    }
}

/// <summary>The free times a booking of the owner's business can move to, with any staff member.</summary>
sealed class GetBookingSlots(IBusinessDirectory directory, ISchedulingDbContext db, BusinessScope scope, Moves moves)
{
    public async Task<Result<PublicSlots>> HandleAsync(
        Guid id, Guid? staffMemberId, string ownerId, CancellationToken cancellation) =>
        await Moves.OwnerBookingAsync(directory, db, scope, ownerId, id, cancellation) is { } booking
            ? await moves.SlotsAsync(booking, staffMemberId, cancellation)
            : Result.NotFound();
}

/// <summary>Moves a booking of the owner's business, at any time; the client gets an email.</summary>
sealed class RescheduleBooking(
    IBusinessDirectory directory, ISchedulingDbContext db, BusinessScope scope, Moves moves, SchedulingTelemetry telemetry)
{
    public async Task<Result<BookingConfirmation>> HandleAsync(
        Guid id, RescheduleBody request, string ownerId, CancellationToken cancellation)
    {
        using var activity = telemetry.StartActivity("bookings.reschedule");

        return await Moves.OwnerBookingAsync(directory, db, scope, ownerId, id, cancellation) is { } booking
            ? await moves.MoveAsync(booking, request, RescheduledBy.Business, activity, cancellation)
            : Result.NotFound();
    }
}

/// <summary>What moving a booking takes, whoever moves it.</summary>
sealed class Moves(
    IBusinessDirectory directory, Availability availability, IBookings bookings, SchedulingTelemetry telemetry,
    TimeProvider time)
{
    // The free slots of the booking's service it can move to, from today, without its own time.
    public async Task<Result<PublicSlots>> SlotsAsync(Booking booking, Guid? staffMemberId, CancellationToken cancellation)
    {
        if (await OfferAsync(booking, staffMemberId, cancellation) is not { } offer)
        {
            return Result.NotFound();
        }
        var slots = await availability.FreeSlotsAsync(
            offer, availability.Today(offer), Availability.DaysAhead, cancellation, moving: booking);
        return Slots.ToPublicSlots(slots, offer);
    }

    public async Task<Result<BookingConfirmation>> MoveAsync(
        Booking booking, RescheduleBody request, RescheduledBy by, System.Diagnostics.Activity? activity,
        CancellationToken cancellation)
    {
        var who = by.ToString().ToLowerInvariant();
        if (request.StartsAt is not { } startsAt)
        {
            return Result.Invalid("startsAt", "Choose a time.");
        }
        if (await OfferAsync(booking, request.StaffMemberId, cancellation) is not { } offer)
        {
            return Result.NotFound();
        }

        // Only a slot that's free for it right now, as when booking. Whoever it's with now comes
        // first, so "anyone" keeps the same person when they're free.
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startsAt, offer.TimeZone).DateTime);
        var slot = (await availability.FreeSlotsAsync(offer, day, 1, cancellation, moving: booking))
            .SingleOrDefault(free => free.Start == startsAt.ToUniversalTime());
        if (slot is null)
        {
            telemetry.Rescheduled(activity, booking, "slot_taken", who);
            return Result.Conflict(Slots.Taken);
        }
        var staffMemberId = slot.FreeStaff.Contains(booking.StaffMemberId) ? booking.StaffMemberId : slot.FreeStaff[0];

        try
        {
            booking.Reschedule(startsAt, staffMemberId, time.GetUtcNow(), by);
        }
        catch (DomainValidationException exception)
        {
            telemetry.Rescheduled(activity, booking, "too_late", who);
            return Result.Conflict(exception.Errors.Values.First()[0]);
        }

        // Someone else may have taken the time a moment ago; the database decides.
        if (await bookings.RescheduleAsync(booking, cancellation) == BookingResult.SlotTaken)
        {
            telemetry.Rescheduled(activity, booking, "slot_taken", who);
            return Result.Conflict(Slots.Taken);
        }

        telemetry.Rescheduled(activity, booking, "rescheduled", who);
        return Slots.ToConfirmation(booking, offer, staffMemberId);
    }

    // The client's own confirmed booking; another client's is as good as missing.
    public static Task<Booking?> ClientBookingAsync(
        ISchedulingDbContext db, string userId, Guid id, CancellationToken cancellation)
    {
        var bookingId = new BookingId(id);
        return db.OfClient(userId)
            .SingleOrDefaultAsync(booking => booking.Id == bookingId && booking.Status == BookingStatus.Confirmed, cancellation);
    }

    // A confirmed booking of the owner's business; another business's isn't seen, by the query filter.
    public static async Task<Booking?> OwnerBookingAsync(
        IBusinessDirectory directory, ISchedulingDbContext db, BusinessScope scope, string ownerId, Guid id,
        CancellationToken cancellation)
    {
        if (await directory.FindOwnedAsync(ownerId, scope, cancellation) is null)
        {
            return null;
        }
        var bookingId = new BookingId(id);
        return await db.Bookings.SingleOrDefaultAsync(
            booking => booking.Id == bookingId && booking.Status == BookingStatus.Confirmed, cancellation);
    }

    // The booking's service at its business, with the staff it may move to; null if it's not offered any more.
    async Task<Offer?> OfferAsync(Booking booking, Guid? staffMemberId, CancellationToken cancellation) =>
        await directory.FindAsync(booking.BusinessId, cancellation) is { } business
            ? await availability.FindOfferAsync(business, booking.ServiceId, Slots.ToStaffId(staffMemberId), cancellation)
            : null;
}
