using System.Security.Claims;
using AspireShowcase.BuildingBlocks.Web;
using AspireShowcase.Identity.PublicClient;
using AspireShowcase.Scheduling.Application.Accounts;

namespace AspireShowcase.Scheduling.Web;

/// <summary>
/// A client deletes their account and their data (user story V1-8): DELETE /me answers 204 when
/// it's done, 409 for an owner, and 503 when the account couldn't be deleted yet.
/// </summary>
static class AccountEndpoints
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapDelete("/me", async (ClaimsPrincipal user, DeleteMyAccount handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(Users.IdOf(user), cancellation)).ToHttp())
        .RequireAuthorization()
        .WithName("DeleteMyAccount");
}
