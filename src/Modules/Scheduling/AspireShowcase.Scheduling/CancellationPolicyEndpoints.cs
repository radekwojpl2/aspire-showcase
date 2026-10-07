using System.Security.Claims;
using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.Identity;
using AspireShowcase.BuildingBlocks.Domain;

namespace AspireShowcase.Scheduling;

/// <param name="NoticeHours">How many hours before a booking clients can still cancel or move it; 0 for until it starts.</param>
record CancellationPolicyBody(int? NoticeHours);

/// <summary>
/// The owner's cancellation policy (user story V1-3), under /businesses/mine/cancellation-policy.
/// New bookings get it; bookings already made keep the one they were made under.
/// </summary>
static class CancellationPolicyEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var policy = api.MapGroup("/businesses/mine/cancellation-policy").RequireAuthorization(IdentityAccess.OwnerPolicy);

        policy.MapGet("/", async (
            ClaimsPrincipal user, IBusinessDirectory directory, SchedulingDbContext db, BusinessScope scope,
            CancellationToken cancellation) =>
            await FindBusinessAsync(user, directory, scope, cancellation) is { } business
                ? Results.Ok(ToBody(await db.PolicyOfAsync(business.Id, cancellation)))
                : Results.NotFound())
        .WithName("GetCancellationPolicy");

        policy.MapPut("/", async (
            CancellationPolicyBody request, ClaimsPrincipal user, IBusinessDirectory directory, SchedulingDbContext db,
            BusinessScope scope, SchedulingTelemetry telemetry, TimeProvider time, CancellationToken cancellation) =>
        {
            using var activity = telemetry.StartActivity("cancellation_policy.set");

            if (await FindBusinessAsync(user, directory, scope, cancellation) is not { } business)
            {
                return Results.NotFound();
            }

            var current = await db.PolicyOfAsync(business.Id, cancellation);
            try
            {
                current.Change(request.NoticeHours, time.GetUtcNow());
            }
            catch (DomainValidationException exception)
            {
                telemetry.PolicyChanged(activity, null, "invalid");
                return Results.ValidationProblem(exception.Errors.ToDictionary());
            }

            // A business's first policy is new; later ones change the row EF Core already tracks.
            if (db.Entry(current).State == Microsoft.EntityFrameworkCore.EntityState.Detached)
            {
                db.CancellationPolicies.Add(current);
            }
            await db.SaveChangesAsync(cancellation);
            telemetry.PolicyChanged(activity, current, "saved");
            return Results.Ok(ToBody(current));
        })
        .WithName("SetCancellationPolicy");
    }

    static CancellationPolicyBody ToBody(CancellationPolicy policy) => new((int)policy.Notice.TotalHours);

    // The signed-in owner's business, with the request scoped to it; null when they have none.
    static async Task<BusinessInfo?> FindBusinessAsync(
        ClaimsPrincipal user, IBusinessDirectory directory, BusinessScope scope, CancellationToken cancellation)
    {
        var ownerId = user.FindFirstValue("sub") ?? throw new InvalidOperationException("The access token has no sub claim.");
        var business = await directory.FindOwnedAsync(ownerId, cancellation);
        scope.BusinessId = business?.Id;
        return business;
    }
}
