using AspireShowcase.BuildingBlocks.Domain;
using AspireShowcase.BusinessSetup.Application.Businesses;
using Microsoft.EntityFrameworkCore;

namespace AspireShowcase.BusinessSetup.Application.Page;

// What the booking page shows about the business (user story V1-6): its address, description
// and logo, so clients know they're in the right place.

/// <summary>Changes the booking page's address and description; either can be left empty.</summary>
sealed class SetPage(IBusinessSetupDbContext db, BusinessTelemetry telemetry)
{
    public async Task<Result<BusinessResponse>> HandleAsync(PageBody request, string userId, CancellationToken cancellation)
    {
        using var activity = telemetry.StartActivity("businesses.page.set");

        if (await db.FindBusinessAsync(userId, cancellation, tracked: true) is not { } business)
        {
            return Result.NotFound();
        }

        try
        {
            business.ChangePage(request.Address, request.Description);
        }
        catch (DomainValidationException exception)
        {
            telemetry.PageChanged(activity, null, "invalid");
            return Result.Invalid(exception);
        }

        await db.SaveChangesAsync(cancellation);
        telemetry.PageChanged(activity, business, "saved");
        return Responses.ToResponse(business);
    }
}

/// <summary>Replaces the logo, or adds the first one; its address changes with each.</summary>
sealed class SetLogo(IBusinessSetupDbContext db, BusinessTelemetry telemetry)
{
    /// <summary>The largest image taken; the endpoint reads no more than one byte past it.</summary>
    public const int MaxBytes = BusinessLogo.MaxBytes;

    public async Task<Result<BusinessResponse>> HandleAsync(byte[] image, string userId, CancellationToken cancellation)
    {
        using var activity = telemetry.StartActivity("businesses.logo.set");

        if (await db.FindBusinessAsync(userId, cancellation, tracked: true) is not { } business)
        {
            return Result.NotFound();
        }

        var logo = await db.Logos.SingleOrDefaultAsync(l => l.BusinessId == business.Id, cancellation);
        try
        {
            if (logo is null)
            {
                db.Logos.Add(BusinessLogo.For(business.Id, image));
            }
            else
            {
                logo.Replace(image);
            }
        }
        catch (DomainValidationException exception)
        {
            telemetry.LogoChanged(activity, null, "invalid");
            return Result.Invalid(exception);
        }

        business.LogoChanged();
        await db.SaveChangesAsync(cancellation);
        telemetry.LogoChanged(activity, business, "saved");
        return Responses.ToResponse(business);
    }
}

sealed class RemoveLogo(IBusinessSetupDbContext db, BusinessTelemetry telemetry)
{
    public async Task<Result<BusinessResponse>> HandleAsync(string userId, CancellationToken cancellation)
    {
        using var activity = telemetry.StartActivity("businesses.logo.remove");

        if (await db.FindBusinessAsync(userId, cancellation, tracked: true) is not { } business)
        {
            return Result.NotFound();
        }

        // Saved together with the business, so it never says there's a logo that's gone.
        if (await db.Logos.SingleOrDefaultAsync(l => l.BusinessId == business.Id, cancellation) is { } logo)
        {
            db.Logos.Remove(logo);
        }
        business.LogoRemoved();
        await db.SaveChangesAsync(cancellation);
        telemetry.LogoChanged(activity, business, "removed");
        return Responses.ToResponse(business);
    }
}

/// <summary>The logo of the business booked at /book/{slug}, for anyone: the booking page shows it.</summary>
sealed class GetLogo(IBusinessSetupDbContext db)
{
    public async Task<Result<LogoImage>> HandleAsync(string slug, CancellationToken cancellation) =>
        await db.Businesses.AsNoTracking()
            .Where(business => business.Slug == slug)
            .Join(db.Logos, business => business.Id, logo => logo.BusinessId,
                (business, logo) => new LogoImage(logo.Image, logo.ContentType, business.LogoVersion))
            .SingleOrDefaultAsync(cancellation) is { } found
            ? found
            : Result.NotFound();
}
