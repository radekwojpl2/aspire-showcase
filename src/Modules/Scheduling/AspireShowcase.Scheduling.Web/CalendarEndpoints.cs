using System.Security.Claims;
using AspireShowcase.BuildingBlocks.Web;
using AspireShowcase.Identity.PublicClient;
using AspireShowcase.Scheduling.Application;
using AspireShowcase.Scheduling.Application.Calendar;
using AspireShowcase.Scheduling.Application.Rescheduling;

namespace AspireShowcase.Scheduling.Web;

/// <summary>
/// The owner's bookings, under /businesses/mine/bookings: the calendar (user story MVP-12) with
/// time off (V1-1), cancelling a booking for its client (MVP-14), and moving one to another time
/// or staff member (V1-4). Only the owner's own business: another's booking answers 404.
/// </summary>
static class CalendarEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var bookings = api.MapGroup("/businesses/mine/bookings").RequireAuthorization(Policies.Owner);

        bookings.MapGet("/", async (
            string? view, string? date, Guid? staffMemberId, ClaimsPrincipal user, GetCalendar handler,
            CancellationToken cancellation) =>
            (await handler.HandleAsync(view, date, staffMemberId, Users.IdOf(user), cancellation))
                .ToHttp(Results.Ok))
        .WithName("GetCalendar");

        // For sickness or emergencies; the client's time is free again at once.
        bookings.MapPost("/{id:guid}/cancel", async (
            Guid id, ClaimsPrincipal user, CancelBooking handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(id, Users.IdOf(user), cancellation)).ToHttp())
        .WithName("CancelBooking");

        bookings.MapGet("/{id:guid}/slots", async (
            Guid id, Guid? staffMemberId, ClaimsPrincipal user, GetBookingSlots handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(id, staffMemberId, Users.IdOf(user), cancellation)).ToHttp(Results.Ok))
        .WithName("GetBookingSlots");

        bookings.MapPost("/{id:guid}/reschedule", async (
            Guid id, RescheduleBody request, ClaimsPrincipal user, RescheduleBooking handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(id, request, Users.IdOf(user), cancellation)).ToHttp(Results.Ok))
        .WithName("RescheduleBooking");
    }
}
