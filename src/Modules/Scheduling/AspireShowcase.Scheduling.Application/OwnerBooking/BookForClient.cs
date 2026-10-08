using AspireShowcase.Scheduling.Application.PublicBooking;

namespace AspireShowcase.Scheduling.Application.OwnerBooking;

/// <summary>
/// The owner books someone who phoned (user story V1-5), so all bookings are in one calendar.
/// The client is a name and email, without an account; they get the same emails as a client who
/// booked online. The free slots are the booking page's own, so someone on the phone gets what a
/// client online would.
/// </summary>
sealed class BookForClient(
    IBusinessDirectory directory, BusinessScope scope, Availability availability, MakeBooking makeBooking,
    SchedulingTelemetry telemetry)
{
    public async Task<Result<BookingConfirmation>> HandleAsync(BookSlot request, string ownerId, CancellationToken cancellation)
    {
        using var activity = telemetry.StartActivity("bookings.book");

        if (request.ServiceId is null ||
            await directory.FindOwnedAsync(ownerId, scope, cancellation) is not { } business ||
            await availability.FindOfferAsync(
                business, new ServiceId(request.ServiceId.Value), Slots.ToStaffId(request.StaffMemberId), cancellation)
                is not { } offer)
        {
            return Result.NotFound();
        }

        // Not tied to anyone's account: the owner's own would make it show as theirs.
        return await makeBooking.HandleAsync(offer, request, userId: null, BookedBy.Business, activity, cancellation);
    }
}
