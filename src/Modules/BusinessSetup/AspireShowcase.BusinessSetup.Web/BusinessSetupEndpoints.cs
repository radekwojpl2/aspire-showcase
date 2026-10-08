using System.Security.Claims;
using AspireShowcase.BuildingBlocks.Web;
using AspireShowcase.BusinessSetup.Application;
using AspireShowcase.BusinessSetup.Application.Businesses;
using AspireShowcase.Identity;

namespace AspireShowcase.BusinessSetup.Web;

static class BusinessSetupEndpoints
{
    /// <summary>
    /// The Business Setup module's endpoints: what an owner configures about their business.
    /// Only for signed-in users: every endpoint needs a Logto access token for this API, which bff
    /// adds (401 without one). Each one hands its request to a use case in Application.
    /// </summary>
    public static void MapEndpoints(IEndpointRouteBuilder api)
    {
        var businesses = api.MapGroup("/businesses").RequireAuthorization();

        // The signed-in user's business, or 404 when they haven't started one.
        businesses.MapGet("/mine", async (ClaimsPrincipal user, GetMyBusiness handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(UserId(user), cancellation)).ToHttp(Results.Ok))
        .WithName("GetMyBusiness");

        // Checked while the user types, so they hear about a taken link before submitting.
        businesses.MapGet("/slug-availability", (string? slug, CheckSlug handler, CancellationToken cancellation) =>
            handler.HandleAsync(slug, cancellation))
        .WithName("GetSlugAvailability");

        // User story MVP-8.
        businesses.MapPost("/", async (
            StartBusiness request, ClaimsPrincipal user, StartBusinessHandler handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(request, UserId(user), cancellation))
                .ToHttp(business => Results.Created("/api/businesses/mine", business)))
        .WithName("CreateBusiness");

        // Where clients can reach the business (V1-3); also how an older business gets one.
        businesses.MapPut("/mine/contact", async (
            ContactBody request, ClaimsPrincipal user, SetContact handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(request, UserId(user), cancellation)).ToHttp(Results.Ok))
        .RequireAuthorization(IdentityAccess.OwnerPolicy)
        .WithName("SetContact");

        // User story MVP-9. Only owners: the owner role comes with starting a business.
        var openingHours = businesses.MapGroup("/mine/opening-hours")
            .RequireAuthorization(IdentityAccess.OwnerPolicy);

        openingHours.MapGet("/", async (ClaimsPrincipal user, GetOpeningHours handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(UserId(user), cancellation)).ToHttp(Results.Ok))
        .WithName("GetOpeningHours");

        // Replaces the whole week at once: the hours are one value object.
        openingHours.MapPut("/", async (
            OpeningHoursBody request, ClaimsPrincipal user, SetOpeningHours handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(request, UserId(user), cancellation)).ToHttp(Results.Ok))
        .WithName("SetOpeningHours");

        // User story MVP-10.
        ServiceEndpoints.Map(businesses.MapGroup("/mine/services").RequireAuthorization(IdentityAccess.OwnerPolicy));

        // User story MVP-11.
        StaffEndpoints.Map(businesses.MapGroup("/mine/staff").RequireAuthorization(IdentityAccess.OwnerPolicy));
    }

    internal static string UserId(ClaimsPrincipal user) =>
        user.FindFirstValue("sub") ?? throw new InvalidOperationException("The access token has no sub claim.");
}
