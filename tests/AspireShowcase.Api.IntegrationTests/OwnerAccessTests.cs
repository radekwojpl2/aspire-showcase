using System.Net;
using System.Net.Http.Json;

namespace AspireShowcase.Api.IntegrationTests;

/// <summary>
/// Who may use the owner's endpoints: only a user with the manage:business permission, which
/// Logto's owner role grants, and then only for their own business.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class OwnerAccessTests(ApiFactory api)
{
    // Every endpoint under /api/businesses/mine except the business itself, which any signed-in
    // user may ask for: it's how the app finds out whether they own one.
    public static TheoryData<string, string> OwnerEndpoints()
    {
        var id = Guid.NewGuid();
        return new()
        {
            { "PUT", "/api/businesses/mine/contact" },
            { "GET", "/api/businesses/mine/opening-hours" },
            { "PUT", "/api/businesses/mine/opening-hours" },
            { "GET", "/api/businesses/mine/services" },
            { "POST", "/api/businesses/mine/services" },
            { "PUT", $"/api/businesses/mine/services/{id}" },
            { "POST", $"/api/businesses/mine/services/{id}/hide" },
            { "POST", $"/api/businesses/mine/services/{id}/show" },
            { "GET", "/api/businesses/mine/staff" },
            { "POST", "/api/businesses/mine/staff" },
            { "PUT", $"/api/businesses/mine/staff/{id}" },
            { "GET", "/api/businesses/mine/bookings" },
            { "POST", $"/api/businesses/mine/bookings/{id}/cancel" },
            { "GET", $"/api/businesses/mine/bookings/{id}/slots" },
            { "POST", $"/api/businesses/mine/bookings/{id}/reschedule" },
            { "GET", "/api/businesses/mine/cancellation-policy" },
            { "PUT", "/api/businesses/mine/cancellation-policy" },
            { "GET", "/api/businesses/mine/time-off" },
            { "POST", "/api/businesses/mine/time-off" },
            { "DELETE", $"/api/businesses/mine/time-off/{id}" },
        };
    }

    [Theory]
    [MemberData(nameof(OwnerEndpoints))]
    public async Task An_owner_endpoint_refuses_a_user_without_the_owner_permission(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) };

        var response = await api.CreateClient(TestUser.Client()).SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(OwnerEndpoints))]
    public async Task An_owner_endpoint_refuses_a_request_without_a_token(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) };

        var response = await api.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_owner_cannot_change_or_hide_a_service_of_another_business()
    {
        var business = await TestBusiness.StartAsync(api);
        var other = api.CreateClient((await TestBusiness.StartAsync(api)).Owner);

        var changed = await other.PutAsJsonAsync($"/api/businesses/mine/services/{business.ServiceId}",
            new { name = "Taken over", durationMinutes = 30, price = 1m, currency = "PLN" });
        var hidden = await other.PostAsync($"/api/businesses/mine/services/{business.ServiceId}/hide", null);

        Assert.Equal(HttpStatusCode.NotFound, changed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        var page = await TestBusiness.ReadAsync<PublicBusiness>(
            await api.CreateClient().GetAsync($"/api/public/businesses/{business.Slug}"));
        Assert.Equal("Haircut", Assert.Single(page.Services).Name);
    }

    [Fact]
    public async Task An_owner_cannot_change_a_staff_member_of_another_business()
    {
        var business = await TestBusiness.StartAsync(api, moreStaff: 1);
        var page = await TestBusiness.ReadAsync<PublicBusiness>(
            await api.CreateClient().GetAsync($"/api/public/businesses/{business.Slug}"));
        var stylist = page.Services.Single().Staff.Single(member => member.Name == "Stylist 1");
        var other = api.CreateClient((await TestBusiness.StartAsync(api)).Owner);

        var changed = await other.PutAsJsonAsync($"/api/businesses/mine/staff/{stylist.Id}",
            new { name = "Taken over", doesAllServices = true });

        Assert.Equal(HttpStatusCode.NotFound, changed.StatusCode);
        var staff = await TestBusiness.ReadAsync<List<PublicStaff>>(
            await api.CreateClient(business.Owner).GetAsync("/api/businesses/mine/staff"));
        Assert.Contains(staff, member => member.Name == "Stylist 1");
    }
}
