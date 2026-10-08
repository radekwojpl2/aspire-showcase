using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;

namespace AspireShowcase.Api.IntegrationTests;

/// <summary>
/// The owner books someone who phoned (V1-5): a name and email, without an account. The test
/// business is open Mondays 09:00 to 12:00 with a one-hour haircut, and the tests run on Monday
/// 2 November 2026 at 08:00.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class OwnerBookingTests(ApiFactory api)
{
    [Fact]
    public async Task An_owner_can_book_a_client_without_an_account()
    {
        var business = await TestBusiness.StartAsync(api);

        var response = await BookAsOwnerAsync(business, business.Owner, TestBusiness.FirstSlot);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var booked = await TestBusiness.ReadAsync<BookingConfirmation>(response);
        Assert.Equal(("Haircut", "2026-11-02", "09:00", "10:00"), (booked.ServiceName, booked.Date, booked.Start, booked.End));
        Assert.Equal($"/api/businesses/mine/bookings/{booked.Id}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task A_booking_by_the_owner_shows_in_the_calendar_and_takes_the_slot()
    {
        var business = await TestBusiness.StartAsync(api);

        var booked = await TestBusiness.ReadAsync<BookingConfirmation>(
            await BookAsOwnerAsync(business, business.Owner, TestBusiness.FirstSlot));

        var calendar = await TestBusiness.ReadAsync<Calendar>(
            await api.CreateClient(business.Owner).GetAsync("/api/businesses/mine/bookings?view=day&date=2026-11-02"));
        Assert.Equal((booked.Id, "Paula Phone"), (Assert.Single(calendar.Bookings).Id, calendar.Bookings[0].ClientName));
        var slots = await TestBusiness.ReadAsync<PublicSlots>(await api.CreateClient()
            .GetAsync($"/api/public/businesses/{business.Slug}/slots?serviceId={business.ServiceId}"));
        Assert.DoesNotContain(slots.Days[0].Slots, slot => slot.Start == "09:00");
    }

    [Fact]
    public async Task An_owner_cannot_book_a_time_that_is_taken()
    {
        var business = await TestBusiness.StartAsync(api);
        await TestBusiness.ReadAsync<BookingConfirmation>(
            await business.BookAsync(api, TestUser.Client(), TestBusiness.FirstSlot));

        var response = await BookAsOwnerAsync(business, business.Owner, TestBusiness.FirstSlot);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_client_cannot_book_through_the_endpoint_of_the_owner()
    {
        var business = await TestBusiness.StartAsync(api);

        var response = await BookAsOwnerAsync(business, TestUser.Client(), TestBusiness.FirstSlot);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_owner_books_only_for_their_own_business()
    {
        var business = await TestBusiness.StartAsync(api);
        var other = await TestBusiness.StartAsync(api);

        // The other owner's business has no such service: theirs is the only business they can book for.
        var response = await BookAsOwnerAsync(business, other.Owner, TestBusiness.FirstSlot);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_name_and_email_of_the_client_are_required()
    {
        var business = await TestBusiness.StartAsync(api);

        var response = await api.CreateClient(business.Owner).PostAsJsonAsync("/api/businesses/mine/bookings", new
        {
            serviceId = business.ServiceId,
            startsAt = TestBusiness.FirstSlot,
            clientName = "",
            clientEmail = "",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.Contains("clientName", problem!.Errors.Keys);
        Assert.Contains("clientEmail", problem.Errors.Keys);
    }

    [Fact]
    public async Task An_owner_can_book_the_staff_member_the_client_asked_for()
    {
        var business = await TestBusiness.StartAsync(api, moreStaff: 1);
        var stylist = (await PublicBusinessAsync(business)).Services.Single().Staff.Single(member => member.Name == "Stylist 1");

        var booked = await TestBusiness.ReadAsync<BookingConfirmation>(
            await BookAsOwnerAsync(business, business.Owner, TestBusiness.FirstSlot, stylist.Id));

        Assert.Equal("Stylist 1", booked.StaffName);
    }

    [Fact]
    public async Task An_owner_cannot_book_a_time_when_the_business_is_closed()
    {
        var business = await TestBusiness.StartAsync(api);

        // Open Mondays 09:00 to 12:00 only: Tuesday isn't offered on the phone either.
        var response = await BookAsOwnerAsync(business, business.Owner, TestBusiness.FirstSlot.AddDays(1));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task An_owner_cannot_book_a_service_the_business_does_not_have()
    {
        var business = await TestBusiness.StartAsync(api);

        var response = await api.CreateClient(business.Owner).PostAsJsonAsync("/api/businesses/mine/bookings", new
        {
            serviceId = Guid.NewGuid(),
            startsAt = TestBusiness.FirstSlot,
            clientName = "Paula Phone",
            clientEmail = "paula@example.com",
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_booking_by_the_owner_can_be_moved_and_cancelled_like_any_other()
    {
        var business = await TestBusiness.StartAsync(api);
        var owner = api.CreateClient(business.Owner);
        var booked = await TestBusiness.ReadAsync<BookingConfirmation>(
            await BookAsOwnerAsync(business, business.Owner, TestBusiness.FirstSlot));

        var moved = await TestBusiness.ReadAsync<BookingConfirmation>(await owner.PostAsJsonAsync(
            $"/api/businesses/mine/bookings/{booked.Id}/reschedule", new { startsAt = TestBusiness.FirstSlot.AddHours(2) }));
        var cancelled = await owner.PostAsync($"/api/businesses/mine/bookings/{booked.Id}/cancel", null);

        Assert.Equal("11:00", moved.Start);
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        var calendar = await TestBusiness.ReadAsync<Calendar>(
            await owner.GetAsync("/api/businesses/mine/bookings?view=day&date=2026-11-02"));
        Assert.Empty(calendar.Bookings);
    }

    [Fact]
    public async Task A_booking_by_the_owner_is_not_among_the_bookings_of_the_owner_as_a_client()
    {
        var business = await TestBusiness.StartAsync(api);

        await TestBusiness.ReadAsync<BookingConfirmation>(await BookAsOwnerAsync(business, business.Owner, TestBusiness.FirstSlot));

        var mine = await TestBusiness.ReadAsync<List<ClientBooking>>(
            await api.CreateClient(business.Owner).GetAsync("/api/me/bookings"));
        Assert.Empty(mine);
    }

    async Task<PublicBusiness> PublicBusinessAsync(TestBusiness business) =>
        await TestBusiness.ReadAsync<PublicBusiness>(await api.CreateClient().GetAsync($"/api/public/businesses/{business.Slug}"));

    Task<HttpResponseMessage> BookAsOwnerAsync(
        TestBusiness business, TestUser user, DateTimeOffset startsAt, Guid? staffMemberId = null) =>
        api.CreateClient(user).PostAsJsonAsync("/api/businesses/mine/bookings", new
        {
            serviceId = business.ServiceId,
            staffMemberId,
            startsAt,
            clientName = "Paula Phone",
            clientEmail = "paula@example.com",
        });
}
