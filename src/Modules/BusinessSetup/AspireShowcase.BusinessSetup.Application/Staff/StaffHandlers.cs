using AspireShowcase.BuildingBlocks.Domain;
using AspireShowcase.BusinessSetup.PublicClient;
using Microsoft.EntityFrameworkCore;

namespace AspireShowcase.BusinessSetup.Application.Staff;

// The owner's staff (user story MVP-11). Every use case is scoped to the signed-in owner's
// business: another business's staff member is as good as missing.

sealed class ListStaff(IBusinessSetupDbContext db)
{
    public async Task<Result<List<StaffResponse>>> HandleAsync(string userId, CancellationToken cancellation)
    {
        if (await db.FindBusinessAsync(userId, cancellation) is not { } business)
        {
            return Result.NotFound();
        }

        var staff = await db.StaffMembers.AsNoTracking()
            .Where(member => member.BusinessId == business.Id)
            .OrderBy(member => member.CreatedAt)
            .ToListAsync(cancellation);
        return staff.Select(member => StaffResponses.ToResponse(member, business)).ToList();
    }
}

sealed class AddStaffMember(IBusinessSetupDbContext db, BusinessTelemetry telemetry, TimeProvider time)
{
    public async Task<Result<StaffResponse>> HandleAsync(StaffBody request, string userId, CancellationToken cancellation)
    {
        using var activity = telemetry.StartActivity("businesses.staff.add");

        if (await db.FindBusinessAsync(userId, cancellation) is not { } business)
        {
            return Result.NotFound();
        }

        StaffMember member;
        try
        {
            member = StaffMember.Add(
                business.Id, request.Name, request.DoesAllServices ?? true, StaffResponses.ToServiceIds(request),
                await StaffResponses.ServiceIdsAsync(db, business, cancellation), StaffResponses.ParseHours(request),
                business.OpeningHours, time.GetUtcNow());
        }
        catch (DomainValidationException exception)
        {
            telemetry.StaffChanged(activity, null, "invalid");
            return Result.Invalid(exception);
        }

        db.StaffMembers.Add(member);
        if (await StaffResponses.SaveAsync(db, cancellation) is { } conflict)
        {
            telemetry.StaffChanged(activity, null, "name_taken");
            return conflict;
        }

        telemetry.StaffChanged(activity, member, "added");
        return StaffResponses.ToResponse(member, business);
    }
}

sealed class ChangeStaffMember(IBusinessSetupDbContext db, BusinessTelemetry telemetry)
{
    public async Task<Result<StaffResponse>> HandleAsync(Guid id, StaffBody request, string userId, CancellationToken cancellation)
    {
        using var activity = telemetry.StartActivity("businesses.staff.change");

        var staffMemberId = new StaffMemberId(id);
        if (await db.FindBusinessAsync(userId, cancellation) is not { } business ||
            await db.StaffMembers.SingleOrDefaultAsync(
                member => member.Id == staffMemberId && member.BusinessId == business.Id, cancellation) is not { } member)
        {
            return Result.NotFound();
        }

        try
        {
            member.Change(
                request.Name, request.DoesAllServices ?? true, StaffResponses.ToServiceIds(request),
                await StaffResponses.ServiceIdsAsync(db, business, cancellation), StaffResponses.ParseHours(request),
                business.OpeningHours);
        }
        catch (DomainValidationException exception)
        {
            telemetry.StaffChanged(activity, null, "invalid");
            return Result.Invalid(exception);
        }

        if (await StaffResponses.SaveAsync(db, cancellation) is { } conflict)
        {
            telemetry.StaffChanged(activity, null, "name_taken");
            return conflict;
        }

        telemetry.StaffChanged(activity, member, "changed");
        return StaffResponses.ToResponse(member, business);
    }
}

static class StaffResponses
{
    const string NameTaken = "Someone on your staff already has this name.";

    // Every service of the business, hidden ones too: a staff member can keep a hidden service.
    public static async Task<IReadOnlyCollection<ServiceId>> ServiceIdsAsync(
        IBusinessSetupDbContext db, Business business, CancellationToken cancellation) =>
        await db.Services.Where(service => service.BusinessId == business.Id)
            .Select(service => service.Id)
            .ToListAsync(cancellation);

    public static IEnumerable<ServiceId>? ToServiceIds(StaffBody request) => request.ServiceIds?.Select(id => new ServiceId(id));

    public static WeeklyHours? ParseHours(StaffBody request) =>
        request.WorkingHours is null ? null : HoursBodies.Parse(request.WorkingHours);

    // Saves, or a conflict when someone else on the staff already has the name.
    public static async Task<Failure?> SaveAsync(IBusinessSetupDbContext db, CancellationToken cancellation)
    {
        try
        {
            await db.SaveChangesAsync(cancellation);
            return null;
        }
        catch (AlreadyExistsException exception) when (exception.Rule == UniqueRule.StaffName)
        {
            return Result.Conflict(new Dictionary<string, string[]> { ["name"] = [NameTaken] });
        }
    }

    public static StaffResponse ToResponse(StaffMember member, Business business) => new(
        member.Id.Value, member.Name, member.UserId == business.OwnerId, member.DoesAllServices,
        member.ServiceIds.Select(id => id.Value).ToList(),
        member.WorkingHours is null ? null : HoursBodies.ToBodies(member.WorkingHours));
}
