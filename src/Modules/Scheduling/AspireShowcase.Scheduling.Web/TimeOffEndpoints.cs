using System.Security.Claims;
using AspireShowcase.BuildingBlocks.Web;
using AspireShowcase.Identity;
using AspireShowcase.Scheduling.Application;
using AspireShowcase.Scheduling.Application.Policies;
using AspireShowcase.Scheduling.Application.TimeOffs;

namespace AspireShowcase.Scheduling.Web;

/// <summary>
/// The owner's settings that only Scheduling enforces: time off (user story V1-1), under
/// /businesses/mine/time-off, and the cancellation policy (V1-3), under
/// /businesses/mine/cancellation-policy. Only the owner's own business.
/// </summary>
static class TimeOffEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var timeOff = api.MapGroup("/businesses/mine/time-off").RequireAuthorization(IdentityAccess.OwnerPolicy);

        // What hasn't ended yet, with the bookings still in it.
        timeOff.MapGet("/", async (ClaimsPrincipal user, ListTimeOff handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(PublicBookingEndpoints.UserId(user), cancellation)).ToHttp(Results.Ok))
        .WithName("GetTimeOff");

        timeOff.MapPost("/", async (
            TimeOffBody request, ClaimsPrincipal user, AddTimeOff handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(request, PublicBookingEndpoints.UserId(user), cancellation))
                .ToHttp(added => Results.Created($"/api/businesses/mine/time-off/{added.Id}", added)))
        .WithName("AddTimeOff");

        timeOff.MapDelete("/{id:guid}", async (
            Guid id, ClaimsPrincipal user, RemoveTimeOff handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(id, PublicBookingEndpoints.UserId(user), cancellation)).ToHttp())
        .WithName("RemoveTimeOff");

        var policy = api.MapGroup("/businesses/mine/cancellation-policy").RequireAuthorization(IdentityAccess.OwnerPolicy);

        policy.MapGet("/", async (ClaimsPrincipal user, GetCancellationPolicy handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(PublicBookingEndpoints.UserId(user), cancellation)).ToHttp(Results.Ok))
        .WithName("GetCancellationPolicy");

        policy.MapPut("/", async (
            CancellationPolicyBody request, ClaimsPrincipal user, SetCancellationPolicy handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(request, PublicBookingEndpoints.UserId(user), cancellation)).ToHttp(Results.Ok))
        .WithName("SetCancellationPolicy");
    }
}
