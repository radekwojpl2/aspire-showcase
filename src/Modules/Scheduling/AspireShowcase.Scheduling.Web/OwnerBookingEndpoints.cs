using System.Security.Claims;
using AspireShowcase.BuildingBlocks.Web;
using AspireShowcase.Identity.PublicClient;
using AspireShowcase.Scheduling.Application;
using AspireShowcase.Scheduling.Application.OwnerBooking;

namespace AspireShowcase.Scheduling.Web;

/// <summary>The owner books someone who phoned (user story V1-5), under /businesses/mine/bookings.</summary>
static class OwnerBookingEndpoints
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost("/businesses/mine/bookings", async (
            BookSlot request, ClaimsPrincipal user, BookForClient handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(request, Users.IdOf(user), cancellation))
                .ToHttp(booked => Results.Created($"/api/businesses/mine/bookings/{booked.Id}", booked)))
        .RequireAuthorization(Policies.Owner)
        .WithName("BookForClient");
}
