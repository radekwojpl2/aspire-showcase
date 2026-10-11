using System.Security.Claims;
using AspireShowcase.BuildingBlocks.Web;
using AspireShowcase.Identity.PublicClient;
using AspireShowcase.Scheduling.Application.Accounts;

namespace AspireShowcase.Scheduling.Web;

/// <summary>
/// The signed-in user's account. GET /me says who they are, 404 once the account is gone. A client
/// deletes their account and their data (user story V1-8): DELETE /me answers 204 when it's done,
/// 409 for an owner, and 503 when the account couldn't be deleted yet.
/// </summary>
static class AccountEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/me", async (ClaimsPrincipal user, GetMe handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(Users.IdOf(user), cancellation)).ToHttp(Results.Ok))
        .RequireAuthorization()
        .WithName("GetMe");

        api.MapDelete("/me", async (ClaimsPrincipal user, DeleteMyAccount handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(Users.IdOf(user), cancellation)).ToHttp())
        .RequireAuthorization()
        .WithName("DeleteMyAccount");
    }
}
