using System.Security.Claims;
using AspireShowcase.BuildingBlocks.Web;
using AspireShowcase.BusinessSetup.Application;
using AspireShowcase.BusinessSetup.Application.Page;
using AspireShowcase.Identity.PublicClient;
using Microsoft.Net.Http.Headers;

namespace AspireShowcase.BusinessSetup.Web;

/// <summary>
/// What the booking page shows about the business (user story V1-6): the owner sets its address,
/// description and logo under /businesses/mine; anyone can fetch the logo, which the booking page
/// links to.
/// </summary>
static class BusinessPageEndpoints
{
    public static void Map(RouteGroupBuilder businesses, IEndpointRouteBuilder api)
    {
        var mine = businesses.MapGroup("/mine").RequireAuthorization(Policies.Owner);

        mine.MapPut("/page", async (PageBody request, ClaimsPrincipal user, SetPage handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(request, Users.IdOf(user), cancellation)).ToHttp(Results.Ok))
        .WithName("SetBusinessPage");

        // The image is the request body, as the browser sends a file, with no form around it.
        mine.MapPut("/logo", async (HttpRequest request, ClaimsPrincipal user, SetLogo handler, CancellationToken cancellation) =>
        {
            // One byte past the limit is enough to tell it's too big, without reading the rest.
            var image = await ReadAtMostAsync(request.Body, SetLogo.MaxBytes + 1, cancellation);
            return (await handler.HandleAsync(image, Users.IdOf(user), cancellation)).ToHttp(Results.Ok);
        })
        .WithName("SetBusinessLogo");

        mine.MapDelete("/logo", async (ClaimsPrincipal user, RemoveLogo handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(Users.IdOf(user), cancellation)).ToHttp(Results.Ok))
        .WithName("RemoveBusinessLogo");

        // For the booking page, so no sign-in. Its address has the version in it (BusinessInfo.LogoUrl),
        // so browsers can keep that one for good; a new logo comes with a new address.
        api.MapGet("/public/businesses/{slug}/logo", async (
            string slug, int? v, HttpContext context, GetLogo handler, CancellationToken cancellation) =>
            (await handler.HandleAsync(slug, cancellation)).ToHttp(logo =>
            {
                context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Response.Headers.CacheControl = v is not null && v == logo.Version
                    ? "public, max-age=31536000, immutable"
                    : "public, no-cache";
                return Results.File(logo.Image, logo.ContentType, entityTag: new EntityTagHeaderValue($"\"{logo.Version}\""));
            }))
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
