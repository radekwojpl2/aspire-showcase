using System.Security.Claims;
using AspireShowcase.BuildingBlocks.Web;
using AspireShowcase.Scheduling.Application;
using AspireShowcase.Scheduling.Application.PublicBooking;

namespace AspireShowcase.Scheduling.Web;

/// <summary>
/// The public booking page's API, under /public/businesses/{slug} (user stories MVP-1 to MVP-4):
/// the business and its free slots for anyone, and booking for signed-in clients.
/// </summary>
static class PublicBookingEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var business = api.MapGroup("/public/businesses/{slug}");

        // MVP-1: no sign-in needed to see what's on offer.
        business.MapGet("/", async (string slug, GetPublicBusiness handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(slug, cancellation)).ToHttp(Results.Ok))
        .WithName("GetPublicBusiness");

        // MVP-1 and MVP-2: free slots for the next 4 weeks, for one staff member or anyone.
        business.MapGet("/slots", async (
            string slug, Guid? serviceId, Guid? staffMemberId, GetFreeSlots handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(slug, serviceId, staffMemberId, cancellation)).ToHttp(Results.Ok))
        .WithName("GetFreeSlots");

        // MVP-4: booking needs a signed-in client, whose account the booking is tied to (MVP-3).
        business.MapPost("/bookings", async (
            string slug, BookSlot request, ClaimsPrincipal user, BookSlotHandler handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(slug, request, user.FindFirstValue("sub"), cancellation))
                .ToHttp(booked => Results.Created($"/api/public/businesses/{slug}/bookings/{booked.Id}", booked)))
        .RequireAuthorization()
        .WithName("BookSlot");
    }

    /// <summary>The signed-in user's ID, which every endpoint that needs sign-in has.</summary>
    public static string UserId(ClaimsPrincipal user) =>
        user.FindFirstValue("sub") ?? throw new InvalidOperationException("The access token has no sub claim.");
}
