using System.Net;
using System.Net.Http.Json;

namespace AspireShowcase.Api.IntegrationTests;

/// <summary>
/// Free slots and booking them (MVP-1 to MVP-4, MVP-7, MVP-12), where PostgreSQL has the last
/// word: its exclusion constraint on bookings and Scheduling's per-business query filter.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BookingTests(ApiFactory api)
{
    [Fact]
    public async Task The_booking_page_offers_only_visible_services()
    {
        var business = await TestBusiness.StartAsync(api);
        var owner = api.CreateClient(business.Owner);
        var hidden = await TestBusiness.ReadAsync<IdResponse>(await owner.PostAsJsonAsync("/api/businesses/mine/services",
            new { name = "Colouring", durationMinutes = 90, price = 120m, currency = "PLN" }));
        await TestBusiness.ReadAsync<object>(await owner.PostAsync($"/api/businesses/mine/services/{hidden.Id}/hide", null));

        var page = await TestBusiness.ReadAsync<PublicBusiness>(
            await api.CreateClient().GetAsync($"/api/public/businesses/{business.Slug}"));

        var service = Assert.Single(page.Services);
        Assert.Equal(business.ServiceId, service.Id);
        Assert.Equal("Olivia Owner", Assert.Single(service.Staff).Name);
    }

    [Fact]
    public async Task Free_slots_follow_the_opening_hours_in_the_business_time_zone()
    {
        var business = await TestBusiness.StartAsync(api);

        var slots = await GetSlotsAsync(business);

        Assert.Equal(TestBusiness.TimeZone, slots.TimeZone);
        // The four Mondays of the next 4 weeks, each 09:00 to 11:00 every 15 minutes.
        Assert.Equal(["2026-11-02", "2026-11-09", "2026-11-16", "2026-11-23"], slots.Days.Select(day => day.Date));
        var monday = slots.Days[0].Slots;
        Assert.Equal(9, monday.Count);
        Assert.Equal(("09:00", TestBusiness.FirstSlot), (monday[0].Start, monday[0].StartsAt));
        Assert.Equal("11:00", monday[^1].Start);
    }

    [Fact]
    public async Task A_booked_slot_is_taken_and_listed_in_my_bookings()
    {
        var business = await TestBusiness.StartAsync(api);
        var client = TestUser.Client();

        var booked = await TestBusiness.ReadAsync<BookingConfirmation>(
            await business.BookAsync(api, client, TestBusiness.FirstSlot));

        Assert.Equal(("2026-11-02", "09:00", "10:00", "Olivia Owner"), (booked.Date, booked.Start, booked.End, booked.StaffName));
        var monday = (await GetSlotsAsync(business)).Days[0].Slots;
        // The hour from 09:00 is taken, so nothing that would overlap it is offered: 10:00 is the first.
        Assert.Equal("10:00", monday[0].Start);
        var mine = await TestBusiness.ReadAsync<List<ClientBooking>>(await api.CreateClient(client).GetAsync("/api/me/bookings"));
        Assert.Equal(booked.Id, Assert.Single(mine).Id);
    }

    [Fact]
    public async Task Only_one_of_many_clients_booking_the_same_slot_at_once_gets_it()
    {
        var business = await TestBusiness.StartAsync(api);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => business.BookAsync(api, TestUser.Client(), TestBusiness.FirstSlot)));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.All(responses.Where(response => response.StatusCode != HttpStatusCode.Created),
            response => Assert.Equal(HttpStatusCode.Conflict, response.StatusCode));
        var calendar = await GetCalendarAsync(business);
        Assert.Single(calendar.Bookings);
    }

    [Fact]
    public async Task Booking_anyone_takes_whoever_is_still_free()
    {
        var business = await TestBusiness.StartAsync(api, moreStaff: 1);

        var first = await TestBusiness.ReadAsync<BookingConfirmation>(
            await business.BookAsync(api, TestUser.Client(), TestBusiness.FirstSlot));
        var second = await TestBusiness.ReadAsync<BookingConfirmation>(
            await business.BookAsync(api, TestUser.Client(), TestBusiness.FirstSlot));
        var third = await business.BookAsync(api, TestUser.Client(), TestBusiness.FirstSlot);

        Assert.NotEqual(first.StaffName, second.StaffName);
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
    }

    [Fact]
    public async Task A_client_can_cancel_only_their_own_booking()
    {
        var business = await TestBusiness.StartAsync(api);
        var client = TestUser.Client();
        var booked = await TestBusiness.ReadAsync<BookingConfirmation>(
            await business.BookAsync(api, client, TestBusiness.FirstSlot));

        var byStranger = await api.CreateClient(TestUser.Client()).PostAsync($"/api/me/bookings/{booked.Id}/cancel", null);
        var byClient = await api.CreateClient(client).PostAsync($"/api/me/bookings/{booked.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.NotFound, byStranger.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, byClient.StatusCode);
        // Free again at once.
        Assert.Equal("09:00", (await GetSlotsAsync(business)).Days[0].Slots[0].Start);
    }

    [Fact]
    public async Task An_owner_sees_and_cancels_only_their_own_business_bookings()
    {
        var mine = await TestBusiness.StartAsync(api);
        var theirs = await TestBusiness.StartAsync(api);
        var myBooking = await TestBusiness.ReadAsync<BookingConfirmation>(
            await mine.BookAsync(api, TestUser.Client(), TestBusiness.FirstSlot));
        var theirBooking = await TestBusiness.ReadAsync<BookingConfirmation>(
            await theirs.BookAsync(api, TestUser.Client(), TestBusiness.FirstSlot));

        var calendar = await GetCalendarAsync(mine);
        var cancelTheirs = await api.CreateClient(mine.Owner)
            .PostAsync($"/api/businesses/mine/bookings/{theirBooking.Id}/cancel", null);

        Assert.Equal(myBooking.Id, Assert.Single(calendar.Bookings).Id);
        Assert.Equal(HttpStatusCode.NotFound, cancelTheirs.StatusCode);
    }

    [Fact]
    public async Task Changing_the_opening_hours_keeps_the_bookings_made()
    {
        var business = await TestBusiness.StartAsync(api);
        var client = TestUser.Client();
        var booked = await TestBusiness.ReadAsync<BookingConfirmation>(
            await business.BookAsync(api, client, TestBusiness.FirstSlot));

        // Closed on Mondays from now on: only new bookings follow the hours.
        await TestBusiness.ReadAsync<object>(await api.CreateClient(business.Owner).PutAsJsonAsync(
            "/api/businesses/mine/opening-hours", new
            {
                timeZone = TestBusiness.TimeZone,
                periods = new[] { new { day = "tuesday", opens = "09:00", closes = "12:00" } },
            }));

        Assert.Equal("2026-11-03", (await GetSlotsAsync(business)).Days[0].Date);
        var kept = Assert.Single((await GetCalendarAsync(business)).Bookings);
        Assert.Equal((booked.Id, "2026-11-02", "09:00"), (kept.Id, kept.Day, kept.Start));
        var mine = await TestBusiness.ReadAsync<List<ClientBooking>>(await api.CreateClient(client).GetAsync("/api/me/bookings"));
        Assert.Equal((booked.Id, "2026-11-02", "09:00"), (Assert.Single(mine).Id, mine[0].Date, mine[0].Start));
    }

    [Fact]
    public async Task Changing_working_hours_keeps_the_staff_member_bookings()
    {
        var business = await TestBusiness.StartAsync(api);
        var owner = api.CreateClient(business.Owner);
        var stylist = await TestBusiness.ReadAsync<IdResponse>(await owner.PostAsJsonAsync("/api/businesses/mine/staff", new
        {
            name = "Sam Stylist",
            doesAllServices = true,
            workingHours = new[] { new { day = "monday", opens = "09:00", closes = "12:00" } },
        }));
        var booked = await TestBusiness.ReadAsync<BookingConfirmation>(
            await business.BookAsync(api, TestUser.Client(), TestBusiness.FirstSlot, stylist.Id));

        // From 11:00 only from now on, which leaves the booking at 09:00 outside the new hours.
        await TestBusiness.ReadAsync<object>(await owner.PutAsJsonAsync($"/api/businesses/mine/staff/{stylist.Id}", new
        {
            name = "Sam Stylist",
            doesAllServices = true,
            workingHours = new[] { new { day = "monday", opens = "11:00", closes = "12:00" } },
        }));

        var slots = await TestBusiness.ReadAsync<PublicSlots>(await api.CreateClient().GetAsync(
            $"/api/public/businesses/{business.Slug}/slots?serviceId={business.ServiceId}&staffMemberId={stylist.Id}"));
        Assert.Equal("11:00", Assert.Single(slots.Days[0].Slots).Start);
        var kept = Assert.Single((await GetCalendarAsync(business)).Bookings);
        Assert.Equal((booked.Id, stylist.Id, "09:00"), (kept.Id, kept.StaffMemberId, kept.Start));
    }

    async Task<PublicSlots> GetSlotsAsync(TestBusiness business) =>
        await TestBusiness.ReadAsync<PublicSlots>(await api.CreateClient()
            .GetAsync($"/api/public/businesses/{business.Slug}/slots?serviceId={business.ServiceId}"));

    async Task<Calendar> GetCalendarAsync(TestBusiness business) =>
        await TestBusiness.ReadAsync<Calendar>(await api.CreateClient(business.Owner)
            .GetAsync("/api/businesses/mine/bookings?view=day&date=2026-11-02"));
}
