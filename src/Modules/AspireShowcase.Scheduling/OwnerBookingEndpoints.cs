using System.Security.Claims;
using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.Identity;

namespace AspireShowcase.Scheduling;

/// <summary>
/// The owner books someone who phoned (user story V1-5), under /businesses/mine/bookings, so all
/// bookings are in one calendar. The client is a name and email, without an account; they get
/// the same emails as a client who booked online.
/// </summary>
/// <remarks>
/// The same free slots as on the booking page, which the owner's calendar reads from
/// /public/businesses/{slug}/slots: someone on the phone gets what a client online would.
/// </remarks>
static class OwnerBookingEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapPost("/businesses/mine/bookings", async (
            BookSlot request, ClaimsPrincipal user, IBusinessDirectory directory, Availability availability,
            SchedulingDbContext db, IServiceScopeFactory scopes, SchedulingTelemetry telemetry, TimeProvider time,
            CancellationToken cancellation) =>
        {
            using var activity = telemetry.StartActivity("bookings.book");

            var ownerId = user.FindFirstValue("sub") ?? throw new InvalidOperationException("The access token has no sub claim.");
            if (request.ServiceId is null ||
                await directory.FindOwnedAsync(ownerId, cancellation) is not { } business ||
                await availability.FindOfferAsync(
                    business, new ServiceId(request.ServiceId.Value), PublicBookingEndpoints.ToStaffId(request.StaffMemberId),
                    cancellation) is not { } offer)
            {
                return Results.NotFound();
            }

            // Not tied to anyone's account: the owner's own would make it show as theirs.
            return await PublicBookingEndpoints.BookAsync(
                offer, request, userId: null, BookedBy.Business, id => $"/api/businesses/mine/bookings/{id}",
                availability, db, scopes, telemetry, activity, time, cancellation);
        })
        .RequireAuthorization(IdentityAccess.OwnerPolicy)
        .WithName("BookForClient");
    }
}
