using System.Net;
using System.Net.Http.Json;

namespace AspireShowcase.Api.IntegrationTests;

/// <summary>
/// Moving a booking to another time (V1-4): by the client within the cancellation policy, or by
/// the owner at any time. The test business is open Mondays 09:00 to 12:00 with a one-hour
/// haircut, and the tests run on Monday 2 November 2026 at 08:00.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RescheduleTests(ApiFactory api)
{
    static readonly DateTimeOffset NineThirty = TestBusiness.FirstSlot.AddMinutes(30);
    static readonly DateTimeOffset Eleven = TestBusiness.FirstSlot.AddHours(2);

    [Fact]
    public async Task A_client_moves_their_booking_and_the_old_time_is_free_again()
    {
        var business = await TestBusiness.StartAsync(api);
        var client = TestUser.Client();
        var booked = await BookAsync(business, client, TestBusiness.FirstSlot);

        var moved = await TestBusiness.ReadAsync<BookingConfirmation>(await MoveAsClientAsync(client, booked.Id, Eleven));

        Assert.Equal(("2026-11-02", "11:00", "12:00"), (moved.Date, moved.Start, moved.End));
        var mine = await TestBusiness.ReadAsync<List<ClientBooking>>(await api.CreateClient(client).GetAsync("/api/me/bookings"));
        Assert.Equal((booked.Id, "11:00"), (Assert.Single(mine).Id, mine[0].Start));
        Assert.Equal("09:00", (await PublicSlotsAsync(business)).Days[0].Slots[0].Start);
    }

    [Fact]
    public async Task A_booking_can_move_into_part_of_its_own_time()
    {
        // 09:30 overlaps the booking's own 09:00 to 10:00: it doesn't count against itself.
        var business = await TestBusiness.StartAsync(api);
        var client = TestUser.Client();
        var booked = await BookAsync(business, client, TestBusiness.FirstSlot);

        var slots = await TestBusiness.ReadAsync<PublicSlots>(
            await api.CreateClient(client).GetAsync($"/api/me/bookings/{booked.Id}/slots"));
        var moved = await TestBusiness.ReadAsync<BookingConfirmation>(await MoveAsClientAsync(client, booked.Id, NineThirty));

        Assert.Contains(slots.Days[0].Slots, slot => slot.Start == "09:30");
        Assert.Equal(("09:30", "10:30"), (moved.Start, moved.End));
    }

    [Fact]
    public async Task A_booking_cannot_move_to_a_time_that_is_taken()
    {
        var business = await TestBusiness.StartAsync(api);
        var client = TestUser.Client();
        var booked = await BookAsync(business, client, TestBusiness.FirstSlot);
        await BookAsync(business, TestUser.Client(), Eleven);

        var response = await MoveAsClientAsync(client, booked.Id, Eleven);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var mine = await TestBusiness.ReadAsync<List<ClientBooking>>(await api.CreateClient(client).GetAsync("/api/me/bookings"));
        Assert.Equal("09:00", Assert.Single(mine).Start);
    }

    [Fact]
    public async Task Only_one_of_a_move_and_a_booking_racing_for_the_same_time_gets_it()
    {
        var business = await TestBusiness.StartAsync(api);
        var client = TestUser.Client();
        var booked = await BookAsync(business, client, TestBusiness.FirstSlot);

        var responses = await Task.WhenAll(
            MoveAsClientAsync(client, booked.Id, Eleven),
            business.BookAsync(api, TestUser.Client(), Eleven));

        Assert.Single(responses, response => response.IsSuccessStatusCode);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_client_cannot_move_a_booking_inside_the_notice_period_but_the_owner_can()
    {
        var business = await TestBusiness.StartAsync(api);
        await TestBusiness.ReadAsync<object>(await api.CreateClient(business.Owner)
            .PutAsJsonAsync("/api/businesses/mine/cancellation-policy", new { noticeHours = 24 }));
        var client = TestUser.Client();
        var booked = await BookAsync(business, client, TestBusiness.FirstSlot);

        var byClient = await MoveAsClientAsync(client, booked.Id, Eleven);
        var byOwner = await api.CreateClient(business.Owner).PostAsJsonAsync(
            $"/api/businesses/mine/bookings/{booked.Id}/reschedule", new { startsAt = Eleven });

        Assert.Equal(HttpStatusCode.Conflict, byClient.StatusCode);
        Assert.Contains("contact the business", await byClient.Content.ReadAsStringAsync());
        Assert.Equal("11:00", (await TestBusiness.ReadAsync<BookingConfirmation>(byOwner)).Start);
    }

    [Fact]
    public async Task The_owner_can_move_a_booking_to_another_staff_member()
    {
        var business = await TestBusiness.StartAsync(api, moreStaff: 1);
        var booked = await BookAsync(business, TestUser.Client(), TestBusiness.FirstSlot);
        var page = await TestBusiness.ReadAsync<PublicBusiness>(
            await api.CreateClient().GetAsync($"/api/public/businesses/{business.Slug}"));
        var stylist = page.Services.Single().Staff.Single(member => member.Name == "Stylist 1");

        var moved = await TestBusiness.ReadAsync<BookingConfirmation>(await api.CreateClient(business.Owner).PostAsJsonAsync(
            $"/api/businesses/mine/bookings/{booked.Id}/reschedule",
            new { startsAt = TestBusiness.FirstSlot, staffMemberId = stylist.Id }));

        Assert.Equal(("09:00", "Stylist 1"), (moved.Start, moved.StaffName));
    }

    [Fact]
    public async Task Another_client_s_or_business_s_booking_cannot_be_moved()
    {
        var business = await TestBusiness.StartAsync(api);
        var booked = await BookAsync(business, TestUser.Client(), TestBusiness.FirstSlot);
        var otherOwner = (await TestBusiness.StartAsync(api)).Owner;

        var byOtherClient = await MoveAsClientAsync(TestUser.Client(), booked.Id, Eleven);
        var byOtherOwner = await api.CreateClient(otherOwner).PostAsJsonAsync(
            $"/api/businesses/mine/bookings/{booked.Id}/reschedule", new { startsAt = Eleven });

        Assert.Equal(HttpStatusCode.NotFound, byOtherClient.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, byOtherOwner.StatusCode);
    }

    async Task<BookingConfirmation> BookAsync(TestBusiness business, TestUser client, DateTimeOffset startsAt) =>
        await TestBusiness.ReadAsync<BookingConfirmation>(await business.BookAsync(api, client, startsAt));

    Task<HttpResponseMessage> MoveAsClientAsync(TestUser client, Guid bookingId, DateTimeOffset startsAt) =>
        api.CreateClient(client).PostAsJsonAsync($"/api/me/bookings/{bookingId}/reschedule", new { startsAt });

    async Task<PublicSlots> PublicSlotsAsync(TestBusiness business) =>
        await TestBusiness.ReadAsync<PublicSlots>(await api.CreateClient()
            .GetAsync($"/api/public/businesses/{business.Slug}/slots?serviceId={business.ServiceId}"));
}
