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

    [Fact]
    public async Task The_owner_sees_the_page_of_their_business_as_they_left_it()
    {
        var business = await TestBusiness.StartAsync(api);
        var owner = api.CreateClient(business.Owner);
        await TestBusiness.ReadAsync<object>(await owner.PutAsJsonAsync("/api/businesses/mine/page",
            new { address = "Main Street 1", description = "Hair for everyone." }));
        await TestBusiness.ReadAsync<object>(await UploadLogoAsync(business.Owner, Png));

        var mine = await TestBusiness.ReadAsync<PublicPage>(await owner.GetAsync("/api/businesses/mine"));

        Assert.Equal(("Main Street 1", "Hair for everyone."), (mine.Address, mine.Description));
        Assert.Equal((await PublicPageAsync(business)).LogoUrl, mine.LogoUrl);
    }

    [Fact]
    public async Task Clearing_the_address_and_description_removes_them_from_the_booking_page()
    {
        var business = await TestBusiness.StartAsync(api);
        var owner = api.CreateClient(business.Owner);
        await TestBusiness.ReadAsync<object>(await owner.PutAsJsonAsync("/api/businesses/mine/page",
            new { address = "Main Street 1", description = "Hair for everyone." }));

        await TestBusiness.ReadAsync<object>(await owner.PutAsJsonAsync("/api/businesses/mine/page",
            new { address = "  ", description = "" }));

        var page = await PublicPageAsync(business);
        Assert.Equal((null, null), (page.Address, page.Description));
    }

    [Fact]
    public async Task A_description_that_is_too_long_is_refused_and_the_page_stays_as_it_was()
    {
        var business = await TestBusiness.StartAsync(api);
        var owner = api.CreateClient(business.Owner);
        await TestBusiness.ReadAsync<object>(await owner.PutAsJsonAsync("/api/businesses/mine/page",
            new { address = "Main Street 1", description = "Hair for everyone." }));

        var response = await owner.PutAsJsonAsync("/api/businesses/mine/page",
            new { address = "Elsewhere", description = new string('a', 1001) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.Contains("description", problem!.Errors.Keys);
        Assert.Equal("Main Street 1", (await PublicPageAsync(business)).Address);
    }

    [Fact]
    public async Task A_logo_over_512_KB_is_refused()
    {
        var business = await TestBusiness.StartAsync(api);
        var image = new byte[512 * 1024 + 1];
        Png.CopyTo(image, 0);

        var response = await UploadLogoAsync(business.Owner, image);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null((await PublicPageAsync(business)).LogoUrl);
    }

    [Fact]
    public async Task The_logo_at_its_versioned_address_is_cached_for_good_and_otherwise_checked_again()
    {
        var business = await TestBusiness.StartAsync(api);
        await TestBusiness.ReadAsync<object>(await UploadLogoAsync(business.Owner, Png));
        var logoUrl = (await PublicPageAsync(business)).LogoUrl!;
        var http = api.CreateClient();

        var versioned = await http.GetAsync(logoUrl);
        var unversioned = await http.GetAsync(logoUrl.Split('?')[0]);

        Assert.Contains("immutable", versioned.Headers.CacheControl!.ToString());
        Assert.True(unversioned.Headers.CacheControl!.NoCache);
        Assert.Equal("\"1\"", versioned.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task A_logo_the_browser_already_has_is_not_sent_again()
    {
        var business = await TestBusiness.StartAsync(api);
        await TestBusiness.ReadAsync<object>(await UploadLogoAsync(business.Owner, Png));
        using var request = new HttpRequestMessage(HttpMethod.Get, (await PublicPageAsync(business)).LogoUrl);
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"1\""));

        var response = await api.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
    }

    [Fact]
    public async Task A_business_without_a_logo_has_none_to_serve()
    {
        var business = await TestBusiness.StartAsync(api);

        var response = await api.CreateClient().GetAsync($"/api/public/businesses/{business.Slug}/logo");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null((await PublicPageAsync(business)).LogoUrl);
    }

    [Fact]
    public async Task An_owner_changes_only_the_page_of_their_own_business()
    {
        var business = await TestBusiness.StartAsync(api);
        var other = await TestBusiness.StartAsync(api);
        await TestBusiness.ReadAsync<object>(await UploadLogoAsync(business.Owner, Png));

        await TestBusiness.ReadAsync<object>(await api.CreateClient(other.Owner).PutAsJsonAsync("/api/businesses/mine/page",
            new { address = "Other Street 2" }));
        await TestBusiness.ReadAsync<object>(await api.CreateClient(other.Owner).DeleteAsync("/api/businesses/mine/logo"));

        var page = await PublicPageAsync(business);
        Assert.Null(page.Address);
        Assert.NotNull(page.LogoUrl);
        Assert.Equal("Other Street 2", (await PublicPageAsync(other)).Address);
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
