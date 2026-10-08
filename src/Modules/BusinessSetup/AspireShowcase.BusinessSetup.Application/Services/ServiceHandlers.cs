using AspireShowcase.BuildingBlocks.Domain;
using AspireShowcase.BusinessSetup.PublicClient;
using Microsoft.EntityFrameworkCore;

namespace AspireShowcase.BusinessSetup.Application.Services;

// The owner's services (user stories MVP-10 and V1-2). Every use case is scoped to the signed-in
// owner's business: another business's service is as good as missing.

/// <summary>All of them, hidden ones too: the owner manages them all. Clients only see the others.</summary>
sealed class ListServices(IBusinessSetupDbContext db)
{
    public async Task<Result<List<ServiceResponse>>> HandleAsync(string userId, CancellationToken cancellation)
    {
        if (await db.FindBusinessAsync(userId, cancellation) is not { } business)
        {
            return Result.NotFound();
        }

        var services = await db.Services.AsNoTracking()
            .Where(service => service.BusinessId == business.Id)
            .OrderBy(service => service.Name)
            .ToListAsync(cancellation);
        return services.Select(ServiceResponses.ToResponse).ToList();
    }
}

sealed class AddService(IBusinessSetupDbContext db, BusinessTelemetry telemetry, TimeProvider time)
{
    public async Task<Result<ServiceResponse>> HandleAsync(ServiceBody request, string userId, CancellationToken cancellation)
    {
        using var activity = telemetry.StartActivity("businesses.services.add");

        if (await db.FindBusinessAsync(userId, cancellation) is not { } business)
        {
            return Result.NotFound();
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
            return Result.Invalid(exception);
        }

        db.Services.Add(service);
        if (await ServiceResponses.SaveAsync(db, cancellation) is { } conflict)
        {
            telemetry.ServiceChanged(activity, null, "name_taken");
            return conflict;
        }

        telemetry.ServiceChanged(activity, service, "added");
        return ServiceResponses.ToResponse(service);
    }
}

sealed class ChangeService(IBusinessSetupDbContext db, BusinessTelemetry telemetry)
{
    public async Task<Result<ServiceResponse>> HandleAsync(
        Guid id, ServiceBody request, string userId, CancellationToken cancellation)
    {
        using var activity = telemetry.StartActivity("businesses.services.change");

        if (await ServiceResponses.FindAsync(db, userId, id, cancellation) is not { } service)
        {
            return Result.NotFound();
        }

        try
        {
            service.Change(request.Name, request.DurationMinutes, request.BufferMinutes, request.Price, request.Currency);
        }
        catch (DomainValidationException exception)
        {
            telemetry.ServiceChanged(activity, null, "invalid");
            return Result.Invalid(exception);
        }

        if (await ServiceResponses.SaveAsync(db, cancellation) is { } conflict)
        {
            telemetry.ServiceChanged(activity, null, "name_taken");
            return conflict;
        }

        telemetry.ServiceChanged(activity, service, "changed");
        return ServiceResponses.ToResponse(service);
    }
}

/// <summary>Hides a service from clients, or shows it again: what the owner does instead of deleting it.</summary>
sealed class SetServiceVisibility(IBusinessSetupDbContext db, BusinessTelemetry telemetry)
{
    public async Task<Result<ServiceResponse>> HandleAsync(Guid id, bool hidden, string userId, CancellationToken cancellation)
    {
        using var activity = telemetry.StartActivity(hidden ? "businesses.services.hide" : "businesses.services.show");

        if (await ServiceResponses.FindAsync(db, userId, id, cancellation) is not { } service)
        {
            return Result.NotFound();
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
        return ServiceResponses.ToResponse(service);
    }
}

static class ServiceResponses
{
    const string NameTaken = "You already have a service with this name.";

    // Only a service of the signed-in owner's business; any other ID is as good as missing.
    public static async Task<Service?> FindAsync(
        IBusinessSetupDbContext db, string ownerId, Guid id, CancellationToken cancellation)
    {
        var serviceId = new ServiceId(id);
        var businessIds = db.Businesses.Where(business => business.OwnerId == ownerId).Select(business => business.Id);
        return await db.Services.SingleOrDefaultAsync(
            service => service.Id == serviceId && businessIds.Contains(service.BusinessId), cancellation);
    }

    // Saves, or a conflict when another service of the business already has the name.
    public static async Task<Failure?> SaveAsync(IBusinessSetupDbContext db, CancellationToken cancellation)
    {
        try
        {
            await db.SaveChangesAsync(cancellation);
            return null;
        }
        catch (AlreadyExistsException exception) when (exception.Rule == UniqueRule.ServiceName)
        {
            return Result.Conflict(new Dictionary<string, string[]> { ["name"] = [NameTaken] });
        }
    }

    public static ServiceResponse ToResponse(Service service) => new(
        service.Id.Value, service.Name, (int)service.Duration.TotalMinutes, (int)service.Buffer.TotalMinutes,
        service.Price.Amount, service.Price.Currency, service.IsHidden);
}
