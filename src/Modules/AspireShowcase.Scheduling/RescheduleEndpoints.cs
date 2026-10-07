using System.Security.Claims;
using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.Identity;
using AspireShowcase.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace AspireShowcase.Scheduling;

/// <param name="StaffMemberId">Who it should be with, or null for anyone who's free (whoever it's with now first).</param>
record RescheduleBody(DateTimeOffset? StartsAt, Guid? StaffMemberId);

/// <summary>
/// Moving a booking to another time (user story V1-4): by the client, under /me/bookings/{id},
/// within the cancellation policy (V1-3); or by the owner, under /businesses/mine/bookings/{id},
/// at any time, such as for bookings inside time off (V1-1). Each side gets the free slots the
/// booking can move to, then moves it to one of them.
/// </summary>
static class RescheduleEndpoints
{
    const string TooLate = "It's too late to move it online: contact the business.";

    public static void Map(IEndpointRouteBuilder api)
    {
        var client = api.MapGroup("/me/bookings/{id:guid}").RequireAuthorization();

        client.MapGet("/slots", async (
            Guid id, Guid? staffMemberId, ClaimsPrincipal user, SchedulingDbContext db, IBusinessDirectory directory,
            Availability availability, CancellationToken cancellation) =>
            await ClientBookingAsync(db, user, id, cancellation) is { } booking
                ? await SlotsAsync(booking, staffMemberId, directory, availability, cancellation)
                : Results.NotFound())
        .WithName("GetMyBookingSlots");

        client.MapPost("/reschedule", async (
            Guid id, RescheduleBody request, ClaimsPrincipal user, SchedulingDbContext db, IBusinessDirectory directory,
            Availability availability, Bookings bookings, SchedulingTelemetry telemetry, TimeProvider time,
            CancellationToken cancellation) =>
        {
            using var activity = telemetry.StartActivity("bookings.reschedule");

            if (await ClientBookingAsync(db, user, id, cancellation) is not { } booking)
            {
                return Results.NotFound();
            }
            // Before looking for the new time: inside the notice, no time would do.
            if (!booking.ClientCanChange(time.GetUtcNow()))
            {
                telemetry.Rescheduled(activity, booking, "too_late", "client");
                return Results.Problem(title: TooLate, statusCode: StatusCodes.Status409Conflict);
            }
            return await RescheduleAsync(
                booking, request, RescheduledBy.Client, directory, availability, bookings, telemetry, activity, time,
                cancellation);
        })
        .WithName("RescheduleMyBooking");

        var owner = api.MapGroup("/businesses/mine/bookings/{id:guid}").RequireAuthorization(IdentityAccess.OwnerPolicy);

        owner.MapGet("/slots", async (
            Guid id, Guid? staffMemberId, ClaimsPrincipal user, SchedulingDbContext db, BusinessScope scope,
            IBusinessDirectory directory, Availability availability, CancellationToken cancellation) =>
            await OwnerBookingAsync(db, scope, directory, user, id, cancellation) is { } booking
                ? await SlotsAsync(booking, staffMemberId, directory, availability, cancellation)
                : Results.NotFound())
        .WithName("GetBookingSlots");

        owner.MapPost("/reschedule", async (
            Guid id, RescheduleBody request, ClaimsPrincipal user, SchedulingDbContext db, BusinessScope scope,
            IBusinessDirectory directory, Availability availability, Bookings bookings, SchedulingTelemetry telemetry,
            TimeProvider time, CancellationToken cancellation) =>
        {
            using var activity = telemetry.StartActivity("bookings.reschedule");

            return await OwnerBookingAsync(db, scope, directory, user, id, cancellation) is { } booking
                ? await RescheduleAsync(
                    booking, request, RescheduledBy.Business, directory, availability, bookings, telemetry, activity,
                    time, cancellation)
                : Results.NotFound();
        })
        .WithName("RescheduleBooking");
    }

    // The free slots of the booking's service it can move to, from today, without its own time.
    static async Task<IResult> SlotsAsync(
        Booking booking, Guid? staffMemberId, IBusinessDirectory directory, Availability availability,
        CancellationToken cancellation)
    {
        if (await OfferAsync(booking, staffMemberId, directory, availability, cancellation) is not { } offer)
        {
            return Results.NotFound();
        }
        var slots = await availability.FreeSlotsAsync(
            offer, availability.Today(offer), Availability.DaysAhead, cancellation, moving: booking);
        return Results.Ok(PublicBookingEndpoints.ToPublicSlots(slots, offer));
    }

    static async Task<IResult> RescheduleAsync(
        Booking booking, RescheduleBody request, RescheduledBy by, IBusinessDirectory directory,
        Availability availability, Bookings bookings, SchedulingTelemetry telemetry, System.Diagnostics.Activity? activity,
        TimeProvider time, CancellationToken cancellation)
    {
        var who = by.ToString().ToLowerInvariant();
        if (request.StartsAt is not { } startsAt)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["startsAt"] = ["Choose a time."] });
        }
        if (await OfferAsync(booking, request.StaffMemberId, directory, availability, cancellation) is not { } offer)
        {
            return Results.NotFound();
        }

        // Only a slot that's free for it right now, as when booking. Whoever it's with now comes
        // first, so "anyone" keeps the same person when they're free.
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startsAt, offer.TimeZone).DateTime);
        var slot = (await availability.FreeSlotsAsync(offer, day, 1, cancellation, moving: booking))
            .SingleOrDefault(free => free.Start == startsAt.ToUniversalTime());
        if (slot is null)
        {
            telemetry.Rescheduled(activity, booking, "slot_taken", who);
            return Results.Problem(title: PublicBookingEndpoints.SlotTaken, statusCode: StatusCodes.Status409Conflict);
        }
        var staffMemberId = slot.FreeStaff.Contains(booking.StaffMemberId) ? booking.StaffMemberId : slot.FreeStaff[0];

        try
        {
            booking.Reschedule(startsAt, staffMemberId, time.GetUtcNow(), by);
        }
        catch (DomainValidationException exception)
        {
            telemetry.Rescheduled(activity, booking, "too_late", who);
            return Results.Problem(title: exception.Errors.Values.First()[0], statusCode: StatusCodes.Status409Conflict);
        }

        // Someone else may have taken the time a moment ago; the database decides.
        if (await bookings.RescheduleAsync(booking, cancellation) == BookingResult.SlotTaken)
        {
            telemetry.Rescheduled(activity, booking, "slot_taken", who);
            return Results.Problem(title: PublicBookingEndpoints.SlotTaken, statusCode: StatusCodes.Status409Conflict);
        }

        telemetry.Rescheduled(activity, booking, "rescheduled", who);
        return Results.Ok(PublicBookingEndpoints.ToConfirmation(booking, offer, staffMemberId));
    }

    // The booking's service at its business, with the staff it may move to; null if it's not offered any more.
    static async Task<Offer?> OfferAsync(
        Booking booking, Guid? staffMemberId, IBusinessDirectory directory, Availability availability,
        CancellationToken cancellation) =>
        await directory.FindAsync(booking.BusinessId, cancellation) is { } business
            ? await availability.FindOfferAsync(
                business, booking.ServiceId, PublicBookingEndpoints.ToStaffId(staffMemberId), cancellation)
            : null;

    // The client's own confirmed booking; another client's answers 404, as if it didn't exist.
    static async Task<Booking?> ClientBookingAsync(
        SchedulingDbContext db, ClaimsPrincipal user, Guid id, CancellationToken cancellation)
    {
        var bookingId = new BookingId(id);
        var userId = user.FindFirstValue("sub") ?? throw new InvalidOperationException("The access token has no sub claim.");
        return await ClientBookingEndpoints.OfClient(db, userId)
            .SingleOrDefaultAsync(booking => booking.Id == bookingId && booking.Status == BookingStatus.Confirmed, cancellation);
    }

    // A confirmed booking of the owner's business; another business's answers 404 by the query filter.
    static async Task<Booking?> OwnerBookingAsync(
        SchedulingDbContext db, BusinessScope scope, IBusinessDirectory directory, ClaimsPrincipal user, Guid id,
        CancellationToken cancellation)
    {
        var ownerId = user.FindFirstValue("sub") ?? throw new InvalidOperationException("The access token has no sub claim.");
        if (await directory.FindOwnedAsync(ownerId, cancellation) is not { } business)
        {
            return null;
        }
        scope.BusinessId = business.Id;
        var bookingId = new BookingId(id);
        return await db.Bookings.SingleOrDefaultAsync(
            booking => booking.Id == bookingId && booking.Status == BookingStatus.Confirmed, cancellation);
    }
}
