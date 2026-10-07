using System.Net.Http.Json;

namespace AspireShowcase.Api.IntegrationTests;

/// <summary>
/// A business clients can book, set up through the owner's endpoints as the React app does: open
/// Mondays 09:00 to 12:00 in Warsaw, with a one-hour haircut done by everyone on the staff.
/// </summary>
public sealed record TestBusiness(TestUser Owner, Guid Id, string Slug, Guid ServiceId)
{
    public const string TimeZone = "Europe/Warsaw";

    /// <summary>The first slot: Monday 2 November 2026, 09:00 in Warsaw (CET, UTC+1).</summary>
    public static readonly DateTimeOffset FirstSlot = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    /// <param name="moreStaff">Staff besides the owner, all doing every service.</param>
    public static async Task<TestBusiness> StartAsync(ApiFactory api, int moreStaff = 0)
    {
        var owner = TestUser.Owner();
        var http = api.CreateClient(owner);
        var slug = $"test-{Guid.NewGuid():N}";

        var started = await http.PostAsJsonAsync("/api/businesses",
            new { name = "Test salon", slug, timeZone = TimeZone, contactEmail = "hello@salon.example", ownerName = "Olivia Owner" });
        var business = await ReadAsync<BusinessResponse>(started);

        var hours = await http.PutAsJsonAsync("/api/businesses/mine/opening-hours", new
        {
            timeZone = TimeZone,
            periods = new[] { new { day = "monday", opens = "09:00", closes = "12:00" } },
        });
        await ReadAsync<object>(hours);

        var service = await ReadAsync<IdResponse>(await http.PostAsJsonAsync("/api/businesses/mine/services",
            new { name = "Haircut", durationMinutes = 60, price = 50m, currency = "PLN" }));

        for (var i = 1; i <= moreStaff; i++)
        {
            await ReadAsync<IdResponse>(await http.PostAsJsonAsync("/api/businesses/mine/staff",
                new { name = $"Stylist {i}", doesAllServices = true }));
        }

        return new TestBusiness(owner, business.Id, slug, service.Id);
    }

    /// <param name="staffMemberId">Who to book, or null for anyone who's free.</param>
    public Task<HttpResponseMessage> BookAsync(
        ApiFactory api, TestUser client, DateTimeOffset startsAt, Guid? staffMemberId = null) =>
        api.CreateClient(client).PostAsJsonAsync($"/api/public/businesses/{Slug}/bookings", new
        {
            serviceId = ServiceId,
            staffMemberId,
            startsAt,
            clientName = "Cleo Client",
            clientEmail = "cleo@example.com",
        });

    /// <summary>The response's body, after checking it succeeded (with the body in the message if it didn't).</summary>
    public static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode,
            $"{(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}

// What the tests read of the API's responses. The API's own records are internal to the modules,
// and a copy here also notices when the JSON changes.

public sealed record IdResponse(Guid Id);

public sealed record BusinessResponse(Guid Id, string Name, string Slug, string TimeZone);

public sealed record PublicBusiness(string Name, string Slug, string TimeZone, List<PublicService> Services);

public sealed record PublicService(Guid Id, string Name, int DurationMinutes, List<PublicStaff> Staff);

public sealed record PublicStaff(Guid Id, string Name);

public sealed record PublicSlots(string TimeZone, List<PublicDay> Days);

public sealed record PublicDay(string Date, List<PublicSlot> Slots);

public sealed record PublicSlot(string Start, DateTimeOffset StartsAt);

public sealed record BookingConfirmation(Guid Id, string ServiceName, string StaffName, string Date, string Start, string End);

public sealed record ClientBooking(Guid Id, string BusinessSlug, string Date, string Start);

public sealed record Calendar(List<CalendarBooking> Bookings);

public sealed record CalendarBooking(Guid Id, string Day, string Start, Guid StaffMemberId, string ClientName);
