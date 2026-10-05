using System.Globalization;
using System.Security.Claims;
using AspireShowcase.Api.Identity;
using AspireShowcase.Api.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AspireShowcase.Api.BusinessSetup;

record StartBusiness(string? Name, string? Slug, string? TimeZone);

record BusinessResponse(Guid Id, string Name, string Slug, string TimeZone, DateTimeOffset CreatedAt);

/// <param name="Problem">Why the link can't be used, when it can't.</param>
record SlugAvailability(string Slug, bool Available, string? Problem);

/// <summary>Opening hours as the API sends and takes them: weekdays by name, times as HH:mm.</summary>
record OpeningHoursBody(string? TimeZone, List<OpeningPeriodBody>? Periods);

record OpeningPeriodBody(string? Day, string? Opens, string? Closes);

static class BusinessSetupEndpoints
{
    const string SlugTaken = "This link is taken. Try another one.";

    /// <summary>
    /// The Business Setup module's endpoints: what an owner configures about their business.
    /// Only for signed-in users: every endpoint needs a Logto access token for this API, which bff
    /// adds (401 without one).
    /// </summary>
    public static void MapBusinessSetup(this IEndpointRouteBuilder api)
    {
        var businesses = api.MapGroup("/businesses").RequireAuthorization();

        // The signed-in user's business, or 404 when they haven't started one.
        businesses.MapGet("/mine", async (ClaimsPrincipal user, AppDbContext db, CancellationToken cancellation) =>
            await FindMineAsync(db.Businesses.AsNoTracking(), user, cancellation) is { } business
                ? Results.Ok(ToResponse(business))
                : Results.NotFound())
        .WithName("GetMyBusiness");

        // Checked while the user types, so they hear about a taken link before submitting.
        businesses.MapGet("/slug-availability", async (string? slug, AppDbContext db, CancellationToken cancellation) =>
        {
            if (BookingSlug.Problem(slug) is { } problem)
            {
                return new SlugAvailability(slug ?? "", false, problem);
            }

            var taken = await db.Businesses.AnyAsync(b => b.Slug == slug, cancellation);
            return new SlugAvailability(slug!, !taken, taken ? SlugTaken : null);
        })
        .WithName("GetSlugAvailability");

        // User story MVP-8.
        businesses.MapPost("/", async (
            StartBusiness request, ClaimsPrincipal user, AppDbContext db, LogtoManagement logto,
            BusinessTelemetry telemetry, TimeProvider time, ILogger<Business> logger, CancellationToken cancellation) =>
        {
            using var activity = telemetry.StartActivity("businesses.create");

            Business business;
            try
            {
                business = Business.Start(request.Name, request.Slug, request.TimeZone, UserId(user), time.GetUtcNow());
            }
            catch (DomainValidationException exception)
            {
                telemetry.Rejected(activity, "invalid");
                return Results.ValidationProblem(exception.Errors.ToDictionary());
            }

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
                if (unique.ConstraintName == BusinessConfiguration.SlugIndex)
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

        // User story MVP-9. Only owners: the owner role comes with starting a business.
        var openingHours = businesses.MapGroup("/mine/opening-hours")
            .RequireAuthorization(IdentityAccess.OwnerPolicy);

        openingHours.MapGet("/", async (ClaimsPrincipal user, AppDbContext db, CancellationToken cancellation) =>
            await FindMineAsync(db.Businesses.AsNoTracking(), user, cancellation) is { } business
                ? Results.Ok(ToBody(business))
                : Results.NotFound())
        .WithName("GetOpeningHours");

        // Replaces the whole week at once: the hours are one value object.
        openingHours.MapPut("/", async (
            OpeningHoursBody request, ClaimsPrincipal user, AppDbContext db, BusinessTelemetry telemetry,
            ILogger<Business> logger, CancellationToken cancellation) =>
        {
            using var activity = telemetry.StartActivity("businesses.opening_hours.set");

            if (await FindMineAsync(db.Businesses, user, cancellation) is not { } business)
            {
                return Results.NotFound();
            }

            try
            {
                business.SetOpeningHours(ParseOpeningHours(request.Periods), request.TimeZone);
            }
            catch (DomainValidationException exception)
            {
                telemetry.OpeningHoursChanged(activity, null, "invalid");
                return Results.ValidationProblem(exception.Errors.ToDictionary());
            }

            await db.SaveChangesAsync(cancellation);
            telemetry.OpeningHoursChanged(activity, business, "saved");
            logger.LogInformation("Opening hours of business {BusinessId} changed", business.Id);
            return Results.Ok(ToBody(business));
        })
        .WithName("SetOpeningHours");
    }

    static Task<Business?> FindMineAsync(IQueryable<Business> businesses, ClaimsPrincipal user, CancellationToken cancellation)
    {
        var userId = UserId(user);
        return businesses.SingleOrDefaultAsync(b => b.OwnerId == userId, cancellation);
    }

    static string UserId(ClaimsPrincipal user) =>
        user.FindFirstValue("sub") ?? throw new InvalidOperationException("The access token has no sub claim.");

    /// <summary>
    /// Turns the request into opening hours. Malformed days and times are reported the same way as
    /// the domain's own rules, under the day they belong to.
    /// </summary>
    static OpeningHours ParseOpeningHours(List<OpeningPeriodBody>? periods)
    {
        var errors = new DomainErrors();
        var parsed = new List<OpeningPeriod>();
        foreach (var period in periods ?? [])
        {
            // Exact names only: Enum.TryParse would also take "1" or "monday,tuesday".
            if (Enum.GetValues<DayOfWeek>().Cast<DayOfWeek?>()
                    .FirstOrDefault(weekday => OpeningHours.FieldName(weekday!.Value) == period.Day) is not { } day)
            {
                errors.Add("periods", $"\"{period.Day}\" isn't a weekday.");
                continue;
            }
            if (!TryParseTime(period.Opens, out var opens) || !TryParseTime(period.Closes, out var closes))
            {
                errors.Add(OpeningHours.FieldName(day), "Enter both times as HH:mm.");
                continue;
            }
            parsed.Add(new OpeningPeriod(day, opens, closes));
        }
        errors.ThrowIfAny();
        return OpeningHours.Create(parsed);
    }

    static bool TryParseTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    static BusinessResponse ToResponse(Business business) =>
        new(business.Id, business.Name, business.Slug, business.TimeZone, business.CreatedAt);

    static OpeningHoursBody ToBody(Business business) =>
        new(business.TimeZone, business.OpeningHours.Periods
            .Select(period => new OpeningPeriodBody(
                OpeningHours.FieldName(period.Day),
                period.Opens.ToString("HH:mm", CultureInfo.InvariantCulture),
                period.Closes.ToString("HH:mm", CultureInfo.InvariantCulture)))
            .ToList());
}
