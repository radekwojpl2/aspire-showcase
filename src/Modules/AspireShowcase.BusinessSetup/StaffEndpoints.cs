using System.Security.Claims;
using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AspireShowcase.BusinessSetup;

/// <param name="ServiceIds">The services they do, unless <paramref name="DoesAllServices"/>.</param>
/// <param name="WorkingHours">Their own hours, or null to work whenever the business is open.</param>
record StaffBody(string? Name, bool? DoesAllServices, List<Guid>? ServiceIds, List<OpeningPeriodBody>? WorkingHours);

/// <param name="IsOwner">The owner's own staff member, created with the business.</param>
record StaffResponse(
    Guid Id, string Name, bool IsOwner, bool DoesAllServices, IReadOnlyList<Guid> ServiceIds,
    List<OpeningPeriodBody>? WorkingHours);

/// <summary>
/// The owner's staff (user story MVP-11), under /businesses/mine/staff. Every request is scoped to
/// the signed-in owner's business: another business's staff member answers 404.
/// </summary>
static class StaffEndpoints
{
    const string NameTaken = "Someone on your staff already has this name.";

    public static void Map(RouteGroupBuilder staff)
    {
        staff.MapGet("/", async (ClaimsPrincipal user, BusinessSetupDbContext db, CancellationToken cancellation) =>
        {
            if (await BusinessSetupEndpoints.FindMineAsync(db.Businesses.AsNoTracking(), user, cancellation) is not { } business)
            {
                return Results.NotFound();
            }

            var list = await db.StaffMembers.AsNoTracking()
                .Where(member => member.BusinessId == business.Id)
                .OrderBy(member => member.CreatedAt)
                .ToListAsync(cancellation);
            return Results.Ok(list.Select(member => ToResponse(member, business)));
        })
        .WithName("GetStaff");

        staff.MapPost("/", async (
            StaffBody request, ClaimsPrincipal user, BusinessSetupDbContext db, BusinessTelemetry telemetry,
            TimeProvider time, CancellationToken cancellation) =>
        {
            using var activity = telemetry.StartActivity("businesses.staff.add");

            if (await BusinessSetupEndpoints.FindMineAsync(db.Businesses.AsNoTracking(), user, cancellation) is not { } business)
            {
                return Results.NotFound();
            }

            StaffMember member;
            try
            {
                member = StaffMember.Add(
                    business.Id, request.Name, request.DoesAllServices ?? true, ToServiceIds(request),
                    await ServiceIdsAsync(db, business, cancellation), ParseHours(request), business.OpeningHours,
                    time.GetUtcNow());
            }
            catch (DomainValidationException exception)
            {
                telemetry.StaffChanged(activity, null, "invalid");
                return Results.ValidationProblem(exception.Errors.ToDictionary());
            }

            db.StaffMembers.Add(member);
            if (await SaveAsync(db, cancellation) is { } conflict)
            {
                telemetry.StaffChanged(activity, null, "name_taken");
                return conflict;
            }

            telemetry.StaffChanged(activity, member, "added");
            return Results.Created($"/api/businesses/mine/staff/{member.Id}", ToResponse(member, business));
        })
        .WithName("AddStaffMember");

        staff.MapPut("/{id:guid}", async (
            Guid id, StaffBody request, ClaimsPrincipal user, BusinessSetupDbContext db, BusinessTelemetry telemetry,
            CancellationToken cancellation) =>
        {
            using var activity = telemetry.StartActivity("businesses.staff.change");

            if (await BusinessSetupEndpoints.FindMineAsync(db.Businesses.AsNoTracking(), user, cancellation) is not { } business ||
                await db.StaffMembers.SingleOrDefaultAsync(
                    member => member.Id == new StaffMemberId(id) && member.BusinessId == business.Id, cancellation)
                    is not { } member)
            {
                return Results.NotFound();
            }

            try
            {
                member.Change(
                    request.Name, request.DoesAllServices ?? true, ToServiceIds(request),
                    await ServiceIdsAsync(db, business, cancellation), ParseHours(request), business.OpeningHours);
            }
            catch (DomainValidationException exception)
            {
                telemetry.StaffChanged(activity, null, "invalid");
                return Results.ValidationProblem(exception.Errors.ToDictionary());
            }

            if (await SaveAsync(db, cancellation) is { } conflict)
            {
                telemetry.StaffChanged(activity, null, "name_taken");
                return conflict;
            }

            telemetry.StaffChanged(activity, member, "changed");
            return Results.Ok(ToResponse(member, business));
        })
        .WithName("ChangeStaffMember");
    }

    // Every service of the business, hidden ones too: a staff member can keep a hidden service.
    static async Task<IReadOnlyCollection<ServiceId>> ServiceIdsAsync(
        BusinessSetupDbContext db, Business business, CancellationToken cancellation) =>
        await db.Services.Where(service => service.BusinessId == business.Id)
            .Select(service => service.Id)
            .ToListAsync(cancellation);

    static IEnumerable<ServiceId>? ToServiceIds(StaffBody request) => request.ServiceIds?.Select(id => new ServiceId(id));

    static WeeklyHours? ParseHours(StaffBody request) =>
        request.WorkingHours is null ? null : BusinessSetupEndpoints.ParseWeeklyHours(request.WorkingHours);

    // Saves, or answers 409 when someone else on the staff already has the name.
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
            ConstraintName: StaffMemberConfiguration.NameIndex,
        })
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["name"] = [NameTaken] },
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    static StaffResponse ToResponse(StaffMember member, Business business) => new(
        member.Id.Value, member.Name, member.UserId == business.OwnerId, member.DoesAllServices,
        member.ServiceIds.Select(id => id.Value).ToList(),
        member.WorkingHours is null ? null : BusinessSetupEndpoints.ToPeriodBodies(member.WorkingHours));
}
