using System.Security.Claims;
using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AspireShowcase.BusinessSetup;

/// <summary>
/// A service as the API takes it: the duration and buffer in minutes (no buffer when left out),
/// the price as an amount and an ISO currency.
/// </summary>
record ServiceBody(string? Name, int? DurationMinutes, int? BufferMinutes, decimal? Price, string? Currency);

record ServiceResponse(
    Guid Id, string Name, int DurationMinutes, int BufferMinutes, decimal Price, string Currency, bool IsHidden);

/// <summary>
/// The owner's services (user story MVP-10), under /businesses/mine/services. Every request is
/// scoped to the signed-in owner's business: another business's service answers 404.
/// </summary>
static class ServiceEndpoints
{
    const string NameTaken = "You already have a service with this name.";

    public static void Map(RouteGroupBuilder services)
    {
        // All of them, hidden ones too: the owner manages them all. Clients will only see the others.
        services.MapGet("/", async (ClaimsPrincipal user, BusinessSetupDbContext db, CancellationToken cancellation) =>
        {
            if (await BusinessSetupEndpoints.FindMineAsync(db.Businesses.AsNoTracking(), user, cancellation) is not { } business)
            {
                return Results.NotFound();
            }

            var list = await db.Services.AsNoTracking()
                .Where(service => service.BusinessId == business.Id)
                .OrderBy(service => service.Name)
                .ToListAsync(cancellation);
            return Results.Ok(list.Select(ToResponse));
        })
        .WithName("GetServices");

        services.MapPost("/", async (
            ServiceBody request, ClaimsPrincipal user, BusinessSetupDbContext db, BusinessTelemetry telemetry,
            TimeProvider time, CancellationToken cancellation) =>
        {
            using var activity = telemetry.StartActivity("businesses.services.add");

            if (await BusinessSetupEndpoints.FindMineAsync(db.Businesses.AsNoTracking(), user, cancellation) is not { } business)
            {
                return Results.NotFound();
            }

            Service service;
            try
            {
                service = Service.Add(
                    business.Id, request.Name, request.DurationMinutes, request.BufferMinutes, request.Price, request.Currency,
                    time.GetUtcNow());
            }
            catch (DomainValidationException exception)
            {
                telemetry.ServiceChanged(activity, null, "invalid");
                return Results.ValidationProblem(exception.Errors.ToDictionary());
            }

            db.Services.Add(service);
            if (await SaveAsync(db, cancellation) is { } conflict)
            {
                telemetry.ServiceChanged(activity, null, "name_taken");
                return conflict;
            }

            telemetry.ServiceChanged(activity, service, "added");
            return Results.Created($"/api/businesses/mine/services/{service.Id}", ToResponse(service));
        })
        .WithName("AddService");

        services.MapPut("/{id:guid}", async (
            Guid id, ServiceBody request, ClaimsPrincipal user, BusinessSetupDbContext db, BusinessTelemetry telemetry,
            CancellationToken cancellation) =>
        {
            using var activity = telemetry.StartActivity("businesses.services.change");

            if (await FindAsync(db, user, id, cancellation) is not { } service)
            {
                return Results.NotFound();
            }

            try
            {
                service.Change(request.Name, request.DurationMinutes, request.BufferMinutes, request.Price, request.Currency);
            }
            catch (DomainValidationException exception)
            {
                telemetry.ServiceChanged(activity, null, "invalid");
                return Results.ValidationProblem(exception.Errors.ToDictionary());
            }

            if (await SaveAsync(db, cancellation) is { } conflict)
            {
                telemetry.ServiceChanged(activity, null, "name_taken");
                return conflict;
            }

            telemetry.ServiceChanged(activity, service, "changed");
            return Results.Ok(ToResponse(service));
        })
        .WithName("ChangeService");

        // Hiding is its own action, not a field to edit: it's what the owner does instead of deleting.
        services.MapPost("/{id:guid}/hide", (Guid id, ClaimsPrincipal user, BusinessSetupDbContext db,
                BusinessTelemetry telemetry, CancellationToken cancellation) =>
            SetVisibilityAsync(id, hidden: true, user, db, telemetry, cancellation))
        .WithName("HideService");

        services.MapPost("/{id:guid}/show", (Guid id, ClaimsPrincipal user, BusinessSetupDbContext db,
                BusinessTelemetry telemetry, CancellationToken cancellation) =>
            SetVisibilityAsync(id, hidden: false, user, db, telemetry, cancellation))
        .WithName("ShowService");
    }

    static async Task<IResult> SetVisibilityAsync(
        Guid id, bool hidden, ClaimsPrincipal user, BusinessSetupDbContext db, BusinessTelemetry telemetry,
        CancellationToken cancellation)
    {
        using var activity = telemetry.StartActivity(hidden ? "businesses.services.hide" : "businesses.services.show");

        if (await FindAsync(db, user, id, cancellation) is not { } service)
        {
            return Results.NotFound();
        }

        if (hidden)
        {
            service.Hide();
        }
        else
        {
            service.Show();
        }
        await db.SaveChangesAsync(cancellation);

        telemetry.ServiceChanged(activity, service, hidden ? "hidden" : "shown");
        return Results.Ok(ToResponse(service));
    }

    // Only a service of the signed-in owner's business; any other ID is as good as missing.
    static async Task<Service?> FindAsync(
        BusinessSetupDbContext db, ClaimsPrincipal user, Guid id, CancellationToken cancellation)
    {
        var serviceId = new ServiceId(id);
        var ownerId = BusinessSetupEndpoints.UserId(user);
        var businessIds = db.Businesses.Where(business => business.OwnerId == ownerId).Select(business => business.Id);
        return await db.Services.SingleOrDefaultAsync(
            service => service.Id == serviceId && businessIds.Contains(service.BusinessId), cancellation);
    }

    // Saves, or answers 409 when another service of the business already has the name.
    static async Task<IResult?> SaveAsync(BusinessSetupDbContext db, CancellationToken cancellation)
    {
        try
        {
            await db.SaveChangesAsync(cancellation);
            return null;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ServiceConfiguration.NameIndex,
        })
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["name"] = [NameTaken] },
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    static ServiceResponse ToResponse(Service service) => new(
        service.Id.Value, service.Name, (int)service.Duration.TotalMinutes, (int)service.Buffer.TotalMinutes, service.Price.Amount,
        service.Price.Currency, service.IsHidden);
}
