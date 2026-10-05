using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Npgsql;

record CreateBusiness(string? Name, string? Slug);

record BusinessResponse(Guid Id, string Name, string Slug, DateTimeOffset CreatedAt);

/// <param name="Problem">Why the link can't be used, when it can't.</param>
record SlugAvailability(string Slug, bool Available, string? Problem);

static class BusinessEndpoints
{
    /// <summary>
    /// Starting a business (user story MVP-8). Only for signed-in users: every endpoint needs
    /// a Logto access token for this API, which bff adds (401 without one).
    /// </summary>
    public static void MapBusinesses(this IEndpointRouteBuilder api)
    {
        var businesses = api.MapGroup("/businesses").RequireAuthorization();

        // The signed-in user's business, or 404 when they haven't started one.
        businesses.MapGet("/mine", async (ClaimsPrincipal user, AppDbContext db, CancellationToken cancellation) =>
            await db.Businesses.AsNoTracking().SingleOrDefaultAsync(b => b.OwnerId == UserId(user), cancellation)
                is { } business
                ? Results.Ok(ToResponse(business))
                : Results.NotFound())
        .WithName("GetMyBusiness");

        // Checked while the user types, so they hear about a taken link before submitting.
        businesses.MapGet("/slug-availability", async (string? slug, AppDbContext db, CancellationToken cancellation) =>
        {
            if (BusinessRules.SlugProblem(slug) is { } problem)
            {
                return new SlugAvailability(slug ?? "", false, problem);
            }

            var taken = await db.Businesses.AnyAsync(b => b.Slug == slug, cancellation);
            return new SlugAvailability(slug!, !taken, taken ? SlugTaken : null);
        })
        .WithName("GetSlugAvailability");

        businesses.MapPost("/", async (
            CreateBusiness request, ClaimsPrincipal user, AppDbContext db, LogtoManagement logto,
            BusinessTelemetry telemetry, ILogger<Program> logger, CancellationToken cancellation) =>
        {
            using var activity = telemetry.StartActivity("businesses.create");

            var errors = new Dictionary<string, string[]>();
            if (BusinessRules.NameProblem(request.Name) is { } nameProblem)
            {
                errors["name"] = [nameProblem];
            }
            if (BusinessRules.SlugProblem(request.Slug) is { } slugProblem)
            {
                errors["slug"] = [slugProblem];
            }
            if (errors.Count > 0)
            {
                telemetry.Rejected(activity, "invalid");
                return Results.ValidationProblem(errors);
            }

            var business = new Business
            {
                Id = Guid.CreateVersion7(),
                Name = request.Name!.Trim(),
                Slug = request.Slug!,
                OwnerId = UserId(user),
                CreatedAt = DateTimeOffset.UtcNow,
            };

            try
            {
                // The execution strategy retries transient database failures, which needs the
                // whole transaction inside it. Giving the role twice does nothing.
                await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    db.ChangeTracker.Clear();
                    await using var transaction = await db.Database.BeginTransactionAsync(cancellation);
                    db.Businesses.Add(business);
                    await db.SaveChangesAsync(cancellation);
                    // Only once the business is saved, and before it's committed: if Logto
                    // fails, the business is rolled back and the user can simply try again.
                    await logto.AssignOwnerRoleAsync(business.OwnerId, cancellation);
                    await transaction.CommitAsync(cancellation);
                });
            }
            catch (DbUpdateException exception) when (
                exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } unique)
            {
                if (unique.ConstraintName == AppDbContext.SlugIndex)
                {
                    telemetry.Rejected(activity, "slug_taken");
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]> { ["slug"] = [SlugTaken] },
                        statusCode: StatusCodes.Status409Conflict);
                }

                telemetry.Rejected(activity, "already_owner");
                return Results.Problem(
                    title: "You already have a business.",
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (LogtoManagementException exception)
            {
                telemetry.Rejected(activity, "logto_unavailable");
                logger.LogError(exception, "Could not give the owner role; the business was not created");
                return Results.Problem(
                    title: "Your business couldn't be set up right now. Please try again.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            telemetry.Created(activity, business);
            logger.LogInformation("Business {BusinessId} started", business.Id);
            return Results.Created("/api/businesses/mine", ToResponse(business));
        })
        .WithName("CreateBusiness");
    }

    const string SlugTaken = "This link is taken. Try another one.";

    static string UserId(ClaimsPrincipal user) =>
        user.FindFirstValue("sub") ?? throw new InvalidOperationException("The access token has no sub claim.");

    static BusinessResponse ToResponse(Business business) =>
        new(business.Id, business.Name, business.Slug, business.CreatedAt);
}
