using AspireShowcase.Identity.PublicClient;
using System.Security.Claims;
using AspireShowcase.BuildingBlocks.Web;
using AspireShowcase.BusinessSetup.Application;
using AspireShowcase.BusinessSetup.Application.Services;

namespace AspireShowcase.BusinessSetup.Web;

/// <summary>
/// The owner's services (user story MVP-10), under /businesses/mine/services. Every request is
/// scoped to the signed-in owner's business: another business's service answers 404.
/// </summary>
static class ServiceEndpoints
{
    public static void Map(RouteGroupBuilder services)
    {
        // All of them, hidden ones too: the owner manages them all. Clients will only see the others.
        services.MapGet("/", async (ClaimsPrincipal user, ListServices handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(Users.IdOf(user), cancellation)).ToHttp(Results.Ok))
        .WithName("GetServices");

        services.MapPost("/", async (
            ServiceBody request, ClaimsPrincipal user, AddService handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(request, Users.IdOf(user), cancellation))
                .ToHttp(service => Results.Created($"/api/businesses/mine/services/{service.Id}", service)))
        .WithName("AddService");

        services.MapPut("/{id:guid}", async (
            Guid id, ServiceBody request, ClaimsPrincipal user, ChangeService handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(id, request, Users.IdOf(user), cancellation)).ToHttp(Results.Ok))
        .WithName("ChangeService");

        // Hiding is its own action, not a field to edit: it's what the owner does instead of deleting.
        services.MapPost("/{id:guid}/hide", async (
            Guid id, ClaimsPrincipal user, SetServiceVisibility handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(id, hidden: true, Users.IdOf(user), cancellation)).ToHttp(Results.Ok))
        .WithName("HideService");

        services.MapPost("/{id:guid}/show", async (
            Guid id, ClaimsPrincipal user, SetServiceVisibility handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(id, hidden: false, Users.IdOf(user), cancellation)).ToHttp(Results.Ok))
        .WithName("ShowService");
    }
}
