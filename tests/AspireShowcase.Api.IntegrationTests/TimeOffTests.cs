using System.Net;
using System.Net.Http.Json;

namespace AspireShowcase.Api.IntegrationTests;

/// <summary>
/// Time off (V1-1): nobody can book it, the bookings already in it are listed for the owner to
/// cancel, and the calendar shows it. The test business is open Mondays 09:00 to 12:00, and the
/// tests run on Monday 2 November 2026 at 08:00.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TimeOffTests(ApiFactory api)
{
    [Fact]
    public async Task Time_off_of_the_business_is_not_offered_to_clients()
    {
        var business = await TestBusiness.StartAsync(api);

        await TestBusiness.ReadAsync<TimeOffEntry>(await AddAsync(business, null, "2026-11-02T09:00", "2026-11-02T10:30", "Team meeting"));

        Assert.Equal("10:30", (await GetSlotsAsync(business)).Days[0].Slots[0].Start);
        var refused = await business.BookAsync(api, TestUser.Client(), TestBusiness.FirstSlot);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    }

    [Fact]
    public async Task A_whole_day_off_leaves_nothing_to_book_that_day()
    {
        var business = await TestBusiness.StartAsync(api);

        await TestBusiness.ReadAsync<TimeOffEntry>(await AddAsync(business, null, "2026-11-02T00:00", "2026-11-03T00:00"));

        Assert.Equal("2026-11-09", (await GetSlotsAsync(business)).Days[0].Date);
    }

    [Fact]
    public async Task Time_off_of_one_staff_member_leaves_the_others_bookable()
    {
        var business = await TestBusiness.StartAsync(api, moreStaff: 1);
        var stylist = await StaffAsync(business, "Stylist 1");

        await TestBusiness.ReadAsync<TimeOffEntry>(await AddAsync(business, stylist, "2026-11-02T09:00", "2026-11-02T12:00", "Dentist"));

        // The owner still has the morning; the stylist has nothing that Monday.
        Assert.Equal("09:00", (await GetSlotsAsync(business)).Days[0].Slots[0].Start);
        Assert.Equal("2026-11-09", (await GetSlotsAsync(business, stylist)).Days[0].Date);
    }

    [Fact]
    public async Task Blocking_a_time_with_bookings_lists_them_until_they_are_cancelled()
    {
        var business = await TestBusiness.StartAsync(api);
        var booked = await TestBusiness.ReadAsync<BookingConfirmation>(
            await business.BookAsync(api, TestUser.Client(), TestBusiness.FirstSlot));

        var added = await TestBusiness.ReadAsync<TimeOffEntry>(
            await AddAsync(business, null, "2026-11-02T09:00", "2026-11-02T12:00", "Closed for a funeral"));

        // The booking stays; the owner sees it next to the time off, and cancels it.
        var inside = Assert.Single(added.Bookings);
        Assert.Equal((booked.Id, "2026-11-02", "09:00", "Cleo Client"), (inside.Id, inside.Day, inside.Start, inside.ClientName));
        Assert.Equal(booked.Id, Assert.Single((await ListAsync(business)).TimeOff).Bookings.Single().Id);
        var owner = api.CreateClient(business.Owner);
        Assert.Equal(HttpStatusCode.NoContent,
            (await owner.PostAsync($"/api/businesses/mine/bookings/{booked.Id}/cancel", null)).StatusCode);
        Assert.Empty(Assert.Single((await ListAsync(business)).TimeOff).Bookings);
    }

    [Fact]
    public async Task A_booking_of_another_staff_member_is_not_listed()
    {
        var business = await TestBusiness.StartAsync(api, moreStaff: 1);
        var stylist = await StaffAsync(business, "Stylist 1");
        await TestBusiness.ReadAsync<BookingConfirmation>(await business.BookAsync(api, TestUser.Client(), TestBusiness.FirstSlot, stylist));

        var ownerAway = await TestBusiness.ReadAsync<TimeOffEntry>(
            await AddAsync(business, await StaffAsync(business, "Olivia Owner"), "2026-11-02T09:00", "2026-11-02T12:00"));

        Assert.Empty(ownerAway.Bookings);
    }

    [Fact]
    public async Task Removing_time_off_makes_it_bookable_again()
    {
        var business = await TestBusiness.StartAsync(api);
        var added = await TestBusiness.ReadAsync<TimeOffEntry>(await AddAsync(business, null, "2026-11-02T09:00", "2026-11-02T12:00"));

        var removed = await api.CreateClient(business.Owner).DeleteAsync($"/api/businesses/mine/time-off/{added.Id}");

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal("09:00", (await GetSlotsAsync(business)).Days[0].Slots[0].Start);
        Assert.Empty((await ListAsync(business)).TimeOff);
    }

    [Fact]
    public async Task The_calendar_shows_time_off_on_every_day_it_covers()
    {
        var business = await TestBusiness.StartAsync(api);
        await TestBusiness.ReadAsync<TimeOffEntry>(await AddAsync(business, null, "2026-11-03T14:00", "2026-11-05T00:00", "Holiday"));

        var week = await TestBusiness.ReadAsync<CalendarWithTimeOff>(await api.CreateClient(business.Owner)
            .GetAsync("/api/businesses/mine/bookings?view=week&date=2026-11-02"));

        Assert.Equal(
            [("2026-11-03", "14:00", "24:00"), ("2026-11-04", "00:00", "24:00")],
            week.TimeOff.Select(piece => (piece.Day, piece.Start, piece.End)));
        Assert.All(week.TimeOff, piece => Assert.Equal("Holiday", piece.Note));
    }

    [Fact]
    public async Task Time_off_of_another_business_is_out_of_reach()
    {
        var business = await TestBusiness.StartAsync(api);
        var other = await TestBusiness.StartAsync(api);
        var added = await TestBusiness.ReadAsync<TimeOffEntry>(await AddAsync(business, null, "2026-11-02T09:00", "2026-11-02T10:00"));

        var removed = await api.CreateClient(other.Owner).DeleteAsync($"/api/businesses/mine/time-off/{added.Id}");

        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
        Assert.Empty((await ListAsync(other)).TimeOff);
        Assert.Single((await ListAsync(business)).TimeOff);
    }

    [Fact]
    public async Task Time_off_for_staff_of_another_business_or_in_a_wrong_format_is_refused()
    {
        var business = await TestBusiness.StartAsync(api);

        var notTheirs = await AddAsync(business, Guid.NewGuid(), "2026-11-02T09:00", "2026-11-02T10:00");
        var badTimes = await AddAsync(business, null, "2 November", "2026-11-02T08:00");

        Assert.Equal(HttpStatusCode.BadRequest, notTheirs.StatusCode);
        Assert.Contains("staffMemberId", await notTheirs.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, badTimes.StatusCode);
        Assert.Contains("from", await badTimes.Content.ReadAsStringAsync());
    }

    Task<HttpResponseMessage> AddAsync(TestBusiness business, Guid? staffMemberId, string from, string to, string? note = null) =>
        api.CreateClient(business.Owner).PostAsJsonAsync("/api/businesses/mine/time-off", new { staffMemberId, from, to, note });

    async Task<TimeOffListing> ListAsync(TestBusiness business) =>
        await TestBusiness.ReadAsync<TimeOffListing>(
            await api.CreateClient(business.Owner).GetAsync("/api/businesses/mine/time-off"));

    async Task<PublicSlots> GetSlotsAsync(TestBusiness business, Guid? staffMemberId = null) =>
        await TestBusiness.ReadAsync<PublicSlots>(await api.CreateClient().GetAsync(
            $"/api/public/businesses/{business.Slug}/slots?serviceId={business.ServiceId}" +
            (staffMemberId is null ? "" : $"&staffMemberId={staffMemberId}")));

    async Task<Guid> StaffAsync(TestBusiness business, string name)
    {
        var page = await TestBusiness.ReadAsync<PublicBusiness>(
            await api.CreateClient().GetAsync($"/api/public/businesses/{business.Slug}"));
        return page.Services.Single().Staff.Single(member => member.Name == name).Id;
    }
}

public sealed record TimeOffListing(string TimeZone, List<TimeOffEntry> TimeOff);

public sealed record TimeOffEntry(Guid Id, Guid? StaffMemberId, string From, string To, string? Note, List<TimeOffBookingEntry> Bookings);

public sealed record TimeOffBookingEntry(Guid Id, string Day, string Start, string End, string ClientName);

public sealed record CalendarWithTimeOff(List<CalendarTimeOffPiece> TimeOff);

public sealed record CalendarTimeOffPiece(Guid Id, string Day, string Start, string End, Guid? StaffMemberId, string? Note);
