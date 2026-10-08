using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;

namespace AspireShowcase.Api.IntegrationTests;

/// <summary>What the booking page shows about a business (V1-6): its address, description and logo.</summary>
[Collection(ApiCollection.Name)]
public sealed class BusinessPageTests(ApiFactory api)
{
    static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    [Fact]
    public async Task The_booking_page_shows_the_address_and_description_the_owner_set()
    {
        var business = await TestBusiness.StartAsync(api);

        var saved = await api.CreateClient(business.Owner).PutAsJsonAsync("/api/businesses/mine/page",
            new { address = "Main Street 1\n00-001 Warsaw", description = "Hair for everyone." });

        await TestBusiness.ReadAsync<object>(saved);
        var page = await PublicPageAsync(business);
        Assert.Equal(("Main Street 1\n00-001 Warsaw", "Hair for everyone."), (page.Address, page.Description));
    }

    [Fact]
    public async Task A_logo_the_owner_uploads_is_served_to_anyone()
    {
        var business = await TestBusiness.StartAsync(api);

        await TestBusiness.ReadAsync<object>(await UploadLogoAsync(business.Owner, Png));

        var page = await PublicPageAsync(business);
        Assert.NotNull(page.LogoUrl);
        var logo = await api.CreateClient().GetAsync(page.LogoUrl);
        Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
        Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await logo.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", logo.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task A_new_logo_gets_a_new_address()
    {
        var business = await TestBusiness.StartAsync(api);
        await TestBusiness.ReadAsync<object>(await UploadLogoAsync(business.Owner, Png));
        var first = (await PublicPageAsync(business)).LogoUrl;

        await TestBusiness.ReadAsync<object>(await UploadLogoAsync(business.Owner, [.. Png, 5]));

        Assert.NotEqual(first, (await PublicPageAsync(business)).LogoUrl);
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_is_refused_as_a_logo()
    {
        var business = await TestBusiness.StartAsync(api);

        var response = await UploadLogoAsync(business.Owner, "<svg></svg>"u8.ToArray(), "image/svg+xml");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("logo", (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>())!.Errors.Keys);
        Assert.Null((await PublicPageAsync(business)).LogoUrl);
    }

    [Fact]
    public async Task A_removed_logo_is_no_longer_served()
    {
        var business = await TestBusiness.StartAsync(api);
        await TestBusiness.ReadAsync<object>(await UploadLogoAsync(business.Owner, Png));
        var logoUrl = (await PublicPageAsync(business)).LogoUrl;

        await TestBusiness.ReadAsync<object>(await api.CreateClient(business.Owner).DeleteAsync("/api/businesses/mine/logo"));

        Assert.Null((await PublicPageAsync(business)).LogoUrl);
        Assert.Equal(HttpStatusCode.NotFound, (await api.CreateClient().GetAsync(logoUrl)).StatusCode);
    }

    [Fact]
    public async Task Only_an_owner_can_change_a_business_page()
    {
        var response = await api.CreateClient(TestUser.Client()).PutAsJsonAsync("/api/businesses/mine/page",
            new { address = "Main Street 1" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    Task<HttpResponseMessage> UploadLogoAsync(TestUser owner, byte[] image, string contentType = "image/png")
    {
        var content = new ByteArrayContent(image);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return api.CreateClient(owner).PutAsync("/api/businesses/mine/logo", content);
    }

    async Task<PublicPage> PublicPageAsync(TestBusiness business) =>
        await TestBusiness.ReadAsync<PublicPage>(await api.CreateClient().GetAsync($"/api/public/businesses/{business.Slug}"));

    sealed record PublicPage(string? Address, string? Description, string? LogoUrl);
}
