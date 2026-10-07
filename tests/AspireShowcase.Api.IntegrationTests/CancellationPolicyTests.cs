using System.Net;
using System.Net.Http.Json;

namespace AspireShowcase.Api.IntegrationTests;

/// <summary>
/// The cancellation policy (V1-3): clients see it before booking, can't cancel inside it, and
/// keep the policy they booked under. The tests run on Monday 2 November 2026 at 08:00, an hour
/// before the first slot.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CancellationPolicyTests(ApiFactory api)
{
    [Fact]
    public async Task Clients_see_the_policy_on_the_booking_page()
    {
        var business = await TestBusiness.StartAsync(api);

        await SetPolicyAsync(business, 24);

        var page = await TestBusiness.ReadAsync<BusinessWithPolicy>(
            await api.CreateClient().GetAsync($"/api/public/businesses/{business.Slug}"));
        Assert.Equal(24, page.CancellationNoticeHours);
    }

    [Fact]
    public async Task A_client_cannot_cancel_inside_the_notice_period_but_the_owner_can()
    {
        var business = await TestBusiness.StartAsync(api);
        await SetPolicyAsync(business, 24);
        var client = TestUser.Client();
        var booked = await TestBusiness.ReadAsync<BookingConfirmation>(
            await business.BookAsync(api, client, TestBusiness.FirstSlot));

        var byClient = await api.CreateClient(client).PostAsync($"/api/me/bookings/{booked.Id}/cancel", null);
        var byOwner = await api.CreateClient(business.Owner).PostAsync($"/api/businesses/mine/bookings/{booked.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.Conflict, byClient.StatusCode);
        Assert.Contains("contact the business", await byClient.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NoContent, byOwner.StatusCode);
    }

    [Fact]
    public async Task A_booking_keeps_the_policy_it_was_made_under()
    {
        var business = await TestBusiness.StartAsync(api);
        var client = TestUser.Client();
        var booked = await TestBusiness.ReadAsync<BookingConfirmation>(
            await business.BookAsync(api, client, TestBusiness.FirstSlot));

        // Stricter from now on: only new bookings get it.
        await SetPolicyAsync(business, 48);

        var cancelled = await api.CreateClient(client).PostAsync($"/api/me/bookings/{booked.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
    }

    [Fact]
    public async Task My_bookings_say_until_when_I_can_change_them_and_how_to_reach_the_business()
    {
        var business = await TestBusiness.StartAsync(api);
        await SetPolicyAsync(business, 24);
        var client = TestUser.Client();
        await TestBusiness.ReadAsync<BookingConfirmation>(
            await business.BookAsync(api, client, TestBusiness.FirstSlot.AddDays(7)));

        var mine = await TestBusiness.ReadAsync<List<MyBooking>>(await api.CreateClient(client).GetAsync("/api/me/bookings"));

        var booking = Assert.Single(mine);
        Assert.Equal(TestBusiness.FirstSlot.AddDays(6), booking.CanChangeUntil);
        Assert.Equal("hello@salon.example", booking.BusinessContactEmail);
    }

    [Fact]
    public async Task The_owner_sees_the_policy_and_a_wrong_one_is_refused()
    {
        var business = await TestBusiness.StartAsync(api);
        var owner = api.CreateClient(business.Owner);

        var none = await TestBusiness.ReadAsync<PolicyBody>(await owner.GetAsync("/api/businesses/mine/cancellation-policy"));
        await SetPolicyAsync(business, 12);
        var saved = await TestBusiness.ReadAsync<PolicyBody>(await owner.GetAsync("/api/businesses/mine/cancellation-policy"));
        var tooLong = await owner.PutAsJsonAsync("/api/businesses/mine/cancellation-policy", new { noticeHours = 1000 });

        Assert.Equal(0, none.NoticeHours);
        Assert.Equal(12, saved.NoticeHours);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    async Task SetPolicyAsync(TestBusiness business, int noticeHours) =>
        await TestBusiness.ReadAsync<PolicyBody>(await api.CreateClient(business.Owner)
            .PutAsJsonAsync("/api/businesses/mine/cancellation-policy", new { noticeHours }));
}

public sealed record PolicyBody(int NoticeHours);

public sealed record BusinessWithPolicy(int CancellationNoticeHours);

public sealed record MyBooking(Guid Id, DateTimeOffset CanChangeUntil, string? BusinessContactEmail);
