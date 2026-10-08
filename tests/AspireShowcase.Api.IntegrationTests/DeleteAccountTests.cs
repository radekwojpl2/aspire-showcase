using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;

namespace AspireShowcase.Api.IntegrationTests;

/// <summary>
/// A client deletes their account and their data (V1-8): their upcoming bookings are cancelled,
/// the rest stay for the business without their name and email, and their Logto account goes.
/// The test business is open Mondays 09:00 to 12:00, and the tests run on Monday 2 November 2026
/// at 08:00.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DeleteAccountTests(ApiFactory api)
{
    [Fact]
    public async Task Deleting_an_account_cancels_its_upcoming_bookings_and_frees_their_time()
    {
        var business = await TestBusiness.StartAsync(api);
        var client = TestUser.Client();
        await TestBusiness.ReadAsync<BookingConfirmation>(await business.BookAsync(api, client, TestBusiness.FirstSlot));

        var response = await api.CreateClient(client).DeleteAsync("/api/me");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var calendar = await CalendarAsync(business);
        Assert.Empty(calendar.Bookings);
        Assert.Contains((await PublicSlotsAsync(business)).Days[0].Slots, slot => slot.Start == "09:00");
    }

    [Fact]
    public async Task Deleting_an_account_cancels_bookings_even_inside_the_cancellation_notice()
    {
        var business = await TestBusiness.StartAsync(api);
        await TestBusiness.ReadAsync<object>(await api.CreateClient(business.Owner)
            .PutAsJsonAsync("/api/businesses/mine/cancellation-policy", new { noticeHours = 24 }));
        var client = TestUser.Client();
        await TestBusiness.ReadAsync<BookingConfirmation>(await business.BookAsync(api, client, TestBusiness.FirstSlot));

        await api.CreateClient(client).DeleteAsync("/api/me");

        Assert.Empty((await CalendarAsync(business)).Bookings);
    }

    [Fact]
    public async Task The_account_in_Logto_is_deleted_and_the_client_has_no_bookings_left()
    {
        var business = await TestBusiness.StartAsync(api);
        var client = TestUser.Client();
        await TestBusiness.ReadAsync<BookingConfirmation>(await business.BookAsync(api, client, TestBusiness.FirstSlot));

        await api.CreateClient(client).DeleteAsync("/api/me");

        Assert.True(api.Accounts.IsDeleted(client.Id));
        Assert.Empty(await TestBusiness.ReadAsync<List<ClientBooking>>(await api.CreateClient(client).GetAsync("/api/me/bookings")));
    }

    [Fact]
    public async Task A_client_without_bookings_can_delete_their_account()
    {
        var client = TestUser.Client();

        var response = await api.CreateClient(client).DeleteAsync("/api/me");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(api.Accounts.IsDeleted(client.Id));
    }

    [Fact]
    public async Task The_bookings_of_other_clients_are_untouched()
    {
        var business = await TestBusiness.StartAsync(api);
        var leaving = TestUser.Client();
        var staying = TestUser.Client();
        await TestBusiness.ReadAsync<BookingConfirmation>(await business.BookAsync(api, leaving, TestBusiness.FirstSlot));
        var kept = await TestBusiness.ReadAsync<BookingConfirmation>(
            await business.BookAsync(api, staying, TestBusiness.FirstSlot.AddHours(2)));

        await api.CreateClient(leaving).DeleteAsync("/api/me");

        var calendar = await CalendarAsync(business);
        Assert.Equal((kept.Id, "Cleo Client", "cleo@example.com"),
            (Assert.Single(calendar.Bookings).Id, calendar.Bookings[0].ClientName, calendar.Bookings[0].ClientEmail));
    }

    [Fact]
    public async Task An_owner_cannot_delete_their_account_while_they_own_a_business()
    {
        var business = await TestBusiness.StartAsync(api);

        var response = await api.CreateClient(business.Owner).DeleteAsync("/api/me");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.StartsWith("You own a business", (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
        Assert.False(api.Accounts.IsDeleted(business.Owner.Id));
    }

    [Fact]
    public async Task When_Logto_is_down_the_data_is_gone_and_asking_again_deletes_the_account()
    {
        var business = await TestBusiness.StartAsync(api);
        var client = TestUser.Client();
        await TestBusiness.ReadAsync<BookingConfirmation>(await business.BookAsync(api, client, TestBusiness.FirstSlot));
        api.Accounts.FailFor(client.Id);

        var first = await api.CreateClient(client).DeleteAsync("/api/me");
        api.Accounts.Recover(client.Id);
        var second = await api.CreateClient(client).DeleteAsync("/api/me");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Empty((await CalendarAsync(business)).Bookings);
        Assert.True(api.Accounts.IsDeleted(client.Id));
    }

    [Fact]
    public async Task Deleting_an_account_needs_a_signed_in_user()
    {
        var response = await api.CreateClient().DeleteAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    async Task<OwnerCalendar> CalendarAsync(TestBusiness business) =>
        await TestBusiness.ReadAsync<OwnerCalendar>(
            await api.CreateClient(business.Owner).GetAsync("/api/businesses/mine/bookings?view=day&date=2026-11-02"));

    async Task<PublicSlots> PublicSlotsAsync(TestBusiness business) =>
        await TestBusiness.ReadAsync<PublicSlots>(await api.CreateClient()
            .GetAsync($"/api/public/businesses/{business.Slug}/slots?serviceId={business.ServiceId}"));
}

/// <summary>
/// A past booking of a client who deleted their account (V1-8) stays for the business. A factory
/// of its own, so the clock can move past the booking without moving it for the other tests.
/// </summary>
public sealed class DeletedAccountHistoryTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_past_booking_stays_for_the_business_without_the_name_and_email_of_the_client()
    {
        var business = await TestBusiness.StartAsync(api);
        var client = TestUser.Client();
        var booked = await TestBusiness.ReadAsync<BookingConfirmation>(
            await business.BookAsync(api, client, TestBusiness.FirstSlot));
        // Monday at 12:00, once the 09:00 to 10:00 booking is over.
        api.Time.Advance(TimeSpan.FromHours(4));

        var deleted = await api.CreateClient(client).DeleteAsync("/api/me");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var calendar = await TestBusiness.ReadAsync<OwnerCalendar>(
            await api.CreateClient(business.Owner).GetAsync("/api/businesses/mine/bookings?view=day&date=2026-11-02"));
        var kept = Assert.Single(calendar.Bookings);
        Assert.Equal((booked.Id, "A former client", null), (kept.Id, kept.ClientName, kept.ClientEmail));
    }
}

public sealed record OwnerCalendar(List<OwnerCalendarBooking> Bookings);

public sealed record OwnerCalendarBooking(Guid Id, string ClientName, string? ClientEmail);
