using AspireShowcase.BuildingBlocks.Domain;
using AspireShowcase.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AspireShowcase.BusinessSetup.Application.Businesses;

// The use cases of the business itself (user stories MVP-8 and MVP-9, and its contact email for V1-3).

/// <summary>The signed-in user's business; not found when they haven't started one.</summary>
sealed class GetMyBusiness(IBusinessSetupDbContext db)
{
    public async Task<Result<BusinessResponse>> HandleAsync(string userId, CancellationToken cancellation) =>
        await db.FindBusinessAsync(userId, cancellation) is { } business
            ? Responses.ToResponse(business)
            : Result.NotFound();
}

/// <summary>Whether a booking link can be used: checked while the user types, before they submit.</summary>
sealed class CheckSlug(IBusinessSetupDbContext db)
{
    public const string Taken = "This link is taken. Try another one.";

    public async Task<SlugAvailability> HandleAsync(string? slug, CancellationToken cancellation)
    {
        if (BookingSlug.Problem(slug) is { } problem)
        {
            return new SlugAvailability(slug ?? "", false, problem);
        }

        var taken = await db.Businesses.AnyAsync(business => business.Slug == slug, cancellation);
        return new SlugAvailability(slug!, !taken, taken ? Taken : null);
    }
}

/// <summary>
/// Starts a business for the signed-in user (user story MVP-8), with them as its first staff
/// member, and gives them the owner role in Logto.
/// </summary>
sealed class StartBusinessHandler(
    IBusinessSetupDbContext db, IOwnerRoles ownerRoles, BusinessTelemetry telemetry, TimeProvider time,
    ILogger<StartBusinessHandler> logger)
{
    public async Task<Result<BusinessResponse>> HandleAsync(StartBusiness request, string userId, CancellationToken cancellation)
    {
        using var activity = telemetry.StartActivity("businesses.create");

        Business business;
        try
        {
            business = Business.Start(request.Name, request.Slug, request.TimeZone, request.ContactEmail, userId, time.GetUtcNow());
        }
        catch (DomainValidationException exception)
        {
            telemetry.Rejected(activity, "invalid");
            return Result.Invalid(exception);
        }

        try
        {
            // Giving the role twice does nothing, so the whole of it can be retried.
            await db.InTransactionAsync(async transaction =>
            {
                db.Businesses.Add(business);
                // The owner is the business's first staff member, so a one-person business can be
                // booked without setting anything else up (user story MVP-11).
                db.StaffMembers.Add(StaffMember.ForOwner(business.Id, business.OwnerId, request.OwnerName, time.GetUtcNow()));
                await db.SaveChangesAsync(transaction);
                // Only once the business is saved, and before it's committed: if Identity can't
                // give the role, the business is rolled back and the user can try again.
                await ownerRoles.AssignOwnerRoleAsync(business.OwnerId, transaction);
            }, cancellation);
        }
        catch (AlreadyExistsException exception) when (exception.Rule == UniqueRule.Slug)
        {
            telemetry.Rejected(activity, "slug_taken");
            return Result.Conflict(new Dictionary<string, string[]> { ["slug"] = [CheckSlug.Taken] });
        }
        catch (AlreadyExistsException)
        {
            telemetry.Rejected(activity, "already_owner");
            return Result.Conflict("You already have a business.");
        }
        catch (OwnerRoleUnavailableException exception)
        {
            telemetry.Rejected(activity, "logto_unavailable");
            logger.LogError(exception, "Could not give the owner role; the business was not created");
            return Result.Unavailable("Your business couldn't be set up right now. Please try again.");
        }

        telemetry.Created(activity, business);
        logger.LogInformation("Business {BusinessId} started", business.Id);
        return Responses.ToResponse(business);
    }
}

/// <summary>Changes where clients can reach the business (V1-3); also how an older business gets one.</summary>
sealed class SetContact(IBusinessSetupDbContext db, BusinessTelemetry telemetry)
{
    public async Task<Result<BusinessResponse>> HandleAsync(ContactBody request, string userId, CancellationToken cancellation)
    {
        using var activity = telemetry.StartActivity("businesses.contact.set");

        if (await db.FindBusinessAsync(userId, cancellation, tracked: true) is not { } business)
        {
            return Result.NotFound();
        }

        try
        {
            business.ChangeContactEmail(request.ContactEmail);
        }
        catch (DomainValidationException exception)
        {
            telemetry.ContactChanged(activity, null, "invalid");
            return Result.Invalid(exception);
        }

        await db.SaveChangesAsync(cancellation);
        telemetry.ContactChanged(activity, business, "saved");
        return Responses.ToResponse(business);
    }
}

/// <summary>The business's weekly opening hours (user story MVP-9).</summary>
sealed class GetOpeningHours(IBusinessSetupDbContext db)
{
    public async Task<Result<OpeningHoursBody>> HandleAsync(string userId, CancellationToken cancellation) =>
        await db.FindBusinessAsync(userId, cancellation) is { } business
            ? Responses.ToOpeningHours(business)
            : Result.NotFound();
}

/// <summary>Replaces the whole week of opening hours at once (user story MVP-9): the hours are one value object.</summary>
sealed class SetOpeningHours(IBusinessSetupDbContext db, BusinessTelemetry telemetry, ILogger<SetOpeningHours> logger)
{
    public async Task<Result<OpeningHoursBody>> HandleAsync(OpeningHoursBody request, string userId, CancellationToken cancellation)
    {
        using var activity = telemetry.StartActivity("businesses.opening_hours.set");

        if (await db.FindBusinessAsync(userId, cancellation, tracked: true) is not { } business)
        {
            return Result.NotFound();
        }

        try
        {
            // Only the hours of the staff who have their own, not the staff members themselves.
            var staffHours = (await db.StaffMembers.AsNoTracking()
                    .Where(member => member.BusinessId == business.Id && member.WorkingHours != null)
                    .ToListAsync(cancellation))
                .Select(member => new StaffHours(member.Name, member.WorkingHours!));
            business.SetOpeningHours(HoursBodies.Parse(request.Periods), request.TimeZone, staffHours);
        }
        catch (DomainValidationException exception)
        {
            telemetry.OpeningHoursChanged(activity, null, "invalid");
            return Result.Invalid(exception);
        }

        await db.SaveChangesAsync(cancellation);
        telemetry.OpeningHoursChanged(activity, business, "saved");
        logger.LogInformation("Opening hours of business {BusinessId} changed", business.Id);
        return Responses.ToOpeningHours(business);
    }
}

static class Responses
{
    public static BusinessResponse ToResponse(Business business) =>
        new(business.Id.Value, business.Name, business.Slug, business.TimeZone, business.ContactEmail, business.CreatedAt);

    public static OpeningHoursBody ToOpeningHours(Business business) =>
        new(business.TimeZone, HoursBodies.ToBodies(business.OpeningHours));
}
