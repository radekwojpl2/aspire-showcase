using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;

namespace AspireShowcase.Api.IntegrationTests;

/// <summary>Starting a business (MVP-8) and who may set it up, against the real database.</summary>
[Collection(ApiCollection.Name)]
public sealed class BusinessSetupTests(ApiFactory api)
{
    [Fact]
    public async Task Starting_a_business_makes_the_user_its_owner()
    {
        var user = TestUser.Owner();
        var http = api.CreateClient(user);
        var slug = NewSlug();

        var started = await http.PostAsJsonAsync("/api/businesses", StartBusiness(slug));

        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var mine = await TestBusiness.ReadAsync<BusinessResponse>(await http.GetAsync("/api/businesses/mine"));
        Assert.Equal(slug, mine.Slug);
        Assert.True(api.OwnerRoles.IsOwner(user.Id));
    }

    [Fact]
    public async Task A_booking_link_that_is_taken_is_refused()
    {
        var slug = NewSlug();
        await api.CreateClient(TestUser.Owner()).PostAsJsonAsync("/api/businesses", StartBusiness(slug));

        var second = await api.CreateClient(TestUser.Owner()).PostAsJsonAsync("/api/businesses", StartBusiness(slug));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var problem = await second.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.Contains("slug", problem!.Errors.Keys);
    }

    [Fact]
    public async Task An_owner_cannot_start_a_second_business()
    {
        var http = api.CreateClient(TestUser.Owner());
        await http.PostAsJsonAsync("/api/businesses", StartBusiness(NewSlug()));

        var second = await http.PostAsJsonAsync("/api/businesses", StartBusiness(NewSlug()));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("You already have a business.", (await second.Content.ReadFromJsonAsync<HttpValidationProblemDetails>())!.Title);
    }

    [Fact]
    public async Task When_Logto_cannot_give_the_owner_role_nothing_is_saved()
    {
        var user = TestUser.Owner();
        api.OwnerRoles.FailFor(user.Id);
        var http = api.CreateClient(user);
        var slug = NewSlug();

        var started = await http.PostAsJsonAsync("/api/businesses", StartBusiness(slug));

        // The business and its owner's staff member were saved, then rolled back with the transaction.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, started.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/businesses/mine")).StatusCode);
        var availability = await TestBusiness.ReadAsync<SlugAvailability>(
            await http.GetAsync($"/api/businesses/slug-availability?slug={slug}"));
        Assert.True(availability.Available);
    }

    [Fact]
    public async Task Requests_without_a_token_are_unauthorized()
    {
        var response = await api.CreateClient().GetAsync("/api/businesses/mine");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Setting_up_a_business_needs_the_owner_permission()
    {
        var response = await api.CreateClient(TestUser.Client()).GetAsync("/api/businesses/mine/opening-hours");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    static string NewSlug() => $"test-{Guid.NewGuid():N}";

    [Fact]
    public async Task A_business_cannot_be_started_without_a_contact_email()
    {
        var response = await api.CreateClient(TestUser.Owner()).PostAsJsonAsync("/api/businesses",
            new { name = "Test salon", slug = NewSlug(), timeZone = TestBusiness.TimeZone, ownerName = "Olivia Owner" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("contactEmail", (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>())!.Errors.Keys);
    }

    [Fact]
    public async Task The_owner_can_change_the_contact_email()
    {
        var business = await TestBusiness.StartAsync(api);
        var owner = api.CreateClient(business.Owner);

        var changed = await owner.PutAsJsonAsync("/api/businesses/mine/contact", new { contactEmail = "bookings@salon.example" });
        var invalid = await owner.PutAsJsonAsync("/api/businesses/mine/contact", new { contactEmail = "nope" });

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var mine = await TestBusiness.ReadAsync<MyBusiness>(await owner.GetAsync("/api/businesses/mine"));
        Assert.Equal("bookings@salon.example", mine.ContactEmail);
    }

    sealed record MyBusiness(string ContactEmail);

    static object StartBusiness(string slug) =>
        new { name = "Test salon", slug, timeZone = TestBusiness.TimeZone, contactEmail = "hello@salon.example", ownerName = "Olivia Owner" };

    sealed record SlugAvailability(string Slug, bool Available);
}
