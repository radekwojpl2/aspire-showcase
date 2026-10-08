using AspireShowcase.Identity.PublicClient;
using System.Security.Claims;
using AspireShowcase.BuildingBlocks.Web;
using AspireShowcase.BusinessSetup.Application;
using AspireShowcase.BusinessSetup.Application.Staff;

namespace AspireShowcase.BusinessSetup.Web;

/// <summary>
/// The owner's staff (user story MVP-11), under /businesses/mine/staff. Every request is scoped to
/// the signed-in owner's business: another business's staff member answers 404.
/// </summary>
static class StaffEndpoints
{
    public static void Map(RouteGroupBuilder staff)
    {
        staff.MapGet("/", async (ClaimsPrincipal user, ListStaff handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(Users.IdOf(user), cancellation)).ToHttp(Results.Ok))
        .WithName("GetStaff");

        staff.MapPost("/", async (
            StaffBody request, ClaimsPrincipal user, AddStaffMember handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(request, Users.IdOf(user), cancellation))
                .ToHttp(member => Results.Created($"/api/businesses/mine/staff/{member.Id}", member)))
        .WithName("AddStaffMember");

        staff.MapPut("/{id:guid}", async (
            Guid id, StaffBody request, ClaimsPrincipal user, ChangeStaffMember handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(id, request, Users.IdOf(user), cancellation)).ToHttp(Results.Ok))
        .WithName("ChangeStaffMember");
    }
}
