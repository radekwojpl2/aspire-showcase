using AspireShowcase.Identity.PublicClient;
using System.Security.Claims;
using AspireShowcase.BuildingBlocks.Web;
using AspireShowcase.Scheduling.Application;
using AspireShowcase.Scheduling.Application.ClientBookings;
using AspireShowcase.Scheduling.Application.Rescheduling;

namespace AspireShowcase.Scheduling.Web;

/// <summary>
/// A client's own bookings (user story MVP-7), under /me/bookings: every business they booked
/// with, cancelling one, and moving one to another time (V1-4), unless the business's cancellation
/// policy says it's too late (V1-3). Another client's booking answers 404, as if it didn't exist.
/// </summary>
static class ClientBookingEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var mine = api.MapGroup("/me/bookings").RequireAuthorization();

        // Upcoming only: past and cancelled bookings aren't listed.
        mine.MapGet("/", (ClaimsPrincipal user, ListMyBookings handler, CancellationToken cancellation) =>
            handler.HandleAsync(Users.IdOf(user), cancellation))
        .WithName("GetMyBookings");

        // Frees the time at once.
        mine.MapPost("/{id:guid}/cancel", async (
            Guid id, ClaimsPrincipal user, CancelMyBooking handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(id, Users.IdOf(user), cancellation)).ToHttp())
        .WithName("CancelMyBooking");

        mine.MapGet("/{id:guid}/slots", async (
            Guid id, Guid? staffMemberId, ClaimsPrincipal user, GetMyBookingSlots handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(id, staffMemberId, Users.IdOf(user), cancellation)).ToHttp(Results.Ok))
        .WithName("GetMyBookingSlots");

        mine.MapPost("/{id:guid}/reschedule", async (
            Guid id, RescheduleBody request, ClaimsPrincipal user, RescheduleMyBooking handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(id, request, Users.IdOf(user), cancellation)).ToHttp(Results.Ok))
        .WithName("RescheduleMyBooking");
    }
}
