using System.Security.Claims;
using AspireShowcase.Identity;
using AspireShowcase.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace AspireShowcase.BusinessSetup;

record PageBody(string? Address, string? Description);

/// <summary>
/// What the booking page shows about the business (user story V1-6): its address, description
/// and logo. The owner sets them under /businesses/mine; anyone can fetch the logo, which the
/// booking page links to.
/// </summary>
static class BusinessPageEndpoints
{
    public static void Map(RouteGroupBuilder businesses, IEndpointRouteBuilder api)
    {
        var mine = businesses.MapGroup("/mine").RequireAuthorization(IdentityAccess.OwnerPolicy);

        mine.MapPut("/page", async (
            PageBody request, ClaimsPrincipal user, BusinessSetupDbContext db, BusinessTelemetry telemetry,
            CancellationToken cancellation) =>
        {
            using var activity = telemetry.StartActivity("businesses.page.set");

            if (await BusinessSetupEndpoints.FindMineAsync(db.Businesses, user, cancellation) is not { } business)
            {
                return Results.NotFound();
            }

            try
            {
                business.ChangePage(request.Address, request.Description);
            }
            catch (DomainValidationException exception)
            {
                telemetry.PageChanged(activity, null, "invalid");
                return Results.ValidationProblem(exception.Errors.ToDictionary());
            }

            await db.SaveChangesAsync(cancellation);
            telemetry.PageChanged(activity, business, "saved");
            return Results.Ok(BusinessSetupEndpoints.ToResponse(business));
        })
        .WithName("SetBusinessPage");

        // The image is the request body, as the browser sends a file, with no form around it.
        mine.MapPut("/logo", async (
            HttpRequest request, ClaimsPrincipal user, BusinessSetupDbContext db, BusinessTelemetry telemetry,
            CancellationToken cancellation) =>
        {
            using var activity = telemetry.StartActivity("businesses.logo.set");

            if (await BusinessSetupEndpoints.FindMineAsync(db.Businesses, user, cancellation) is not { } business)
            {
                return Results.NotFound();
            }

            // One byte past the limit is enough to tell it's too big, without reading the rest.
            var image = await ReadAtMostAsync(request.Body, BusinessLogo.MaxBytes + 1, cancellation);
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
                return Results.ValidationProblem(exception.Errors.ToDictionary());
            }

            business.LogoChanged();
            await db.SaveChangesAsync(cancellation);
            telemetry.LogoChanged(activity, business, "saved");
            return Results.Ok(BusinessSetupEndpoints.ToResponse(business));
        })
        .WithName("SetBusinessLogo");

        mine.MapDelete("/logo", async (
            ClaimsPrincipal user, BusinessSetupDbContext db, BusinessTelemetry telemetry, CancellationToken cancellation) =>
        {
            using var activity = telemetry.StartActivity("businesses.logo.remove");

            if (await BusinessSetupEndpoints.FindMineAsync(db.Businesses, user, cancellation) is not { } business)
            {
                return Results.NotFound();
            }

            // Saved together with the business, so it never says there's a logo that's gone.
            if (await db.Logos.SingleOrDefaultAsync(l => l.BusinessId == business.Id, cancellation) is { } logo)
            {
                db.Logos.Remove(logo);
            }
            business.LogoRemoved();
            await db.SaveChangesAsync(cancellation);
            telemetry.LogoChanged(activity, business, "removed");
            return Results.Ok(BusinessSetupEndpoints.ToResponse(business));
        })
        .WithName("RemoveBusinessLogo");

        // For the booking page, so no sign-in. Its address has the version in it (BusinessInfo.LogoUrl),
        // so browsers can keep that one for good; a new logo comes with a new address.
        api.MapGet("/public/businesses/{slug}/logo", async (
            string slug, int? v, HttpContext context, BusinessSetupDbContext db, CancellationToken cancellation) =>
        {
            var found = await db.Businesses.AsNoTracking()
                .Where(business => business.Slug == slug)
                .Join(db.Logos, business => business.Id, logo => logo.BusinessId,
                    (business, logo) => new { business.LogoVersion, logo.Image, logo.ContentType })
                .SingleOrDefaultAsync(cancellation);
            if (found is null)
            {
                return Results.NotFound();
            }

            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.CacheControl = v == found.LogoVersion
                ? "public, max-age=31536000, immutable"
                : "public, no-cache";
            return Results.File(
                found.Image, found.ContentType, entityTag: new EntityTagHeaderValue($"\"{found.LogoVersion}\""));
        })
        .AllowAnonymous()
        .WithName("GetBusinessLogo");
    }

    static async Task<byte[]> ReadAtMostAsync(Stream body, int limit, CancellationToken cancellation)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while (buffer.Length < limit &&
               (read = await body.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, limit - buffer.Length)), cancellation)) > 0)
        {
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
