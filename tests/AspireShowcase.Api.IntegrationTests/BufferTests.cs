using System.Net;
using System.Net.Http.Json;

namespace AspireShowcase.Api.IntegrationTests;

/// <summary>
/// The buffer after a service (V1-2): time nobody can book, kept by the free slots and, when two
/// bookings race, by PostgreSQL's exclusion constraint.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BufferTests(ApiFactory api)
{
    [Fact]
    public async Task A_service_s_buffer_is_kept_free_after_each_booking()
    {
        var business = await TestBusiness.StartAsync(api);
        await SetBufferAsync(business, business.ServiceId, "Haircut", 60, bufferMinutes: 15);

        var booked = await TestBusiness.ReadAsync<BookingConfirmation>(
            await business.BookAsync(api, TestUser.Client(), TestBusiness.FirstSlot));

        // The client sees the hour they booked; the next slot waits for the clean-up.
        Assert.Equal(("09:00", "10:00"), (booked.Start, booked.End));
        Assert.Equal("10:15", (await GetSlotsAsync(business, business.ServiceId)).Days[0].Slots[0].Start);
        var tooEarly = await business.BookAsync(api, TestUser.Client(), TestBusiness.FirstSlot.AddHours(1));
        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);
    }

    [Fact]
    public async Task The_database_keeps_the_buffer_when_two_bookings_race()
    {
        // Both slots are free when the requests start; only the constraint can refuse one.
        var business = await TestBusiness.StartAsync(api);
        await SetBufferAsync(business, business.ServiceId, "Haircut", 60, bufferMinutes: 15);
        var trim = await TestBusiness.ReadAsync<ServiceWithBuffer>(await api.CreateClient(business.Owner)
            .PostAsJsonAsync("/api/businesses/mine/services",
                new { name = "Trim", durationMinutes = 15, price = 20m, currency = "PLN" }));

        var responses = await Task.WhenAll(
            business.BookAsync(api, TestUser.Client(), TestBusiness.FirstSlot),
            BookAsync(business, trim.Id, TestBusiness.FirstSlot.AddHours(1)));

        // The haircut's buffer runs until 10:15, so the trim at 10:00 and the haircut can't both be booked.
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_owner_sees_the_buffer_of_their_services()
    {
        var business = await TestBusiness.StartAsync(api);

        var changed = await SetBufferAsync(business, business.ServiceId, "Haircut", 60, bufferMinutes: 10);
        var services = await TestBusiness.ReadAsync<List<ServiceWithBuffer>>(
            await api.CreateClient(business.Owner).GetAsync("/api/businesses/mine/services"));

        Assert.Equal(10, changed.BufferMinutes);
        Assert.Equal(10, Assert.Single(services).BufferMinutes);
    }

    async Task<ServiceWithBuffer> SetBufferAsync(
        TestBusiness business, Guid serviceId, string name, int durationMinutes, int bufferMinutes) =>
        await TestBusiness.ReadAsync<ServiceWithBuffer>(await api.CreateClient(business.Owner).PutAsJsonAsync(
            $"/api/businesses/mine/services/{serviceId}",
            new { name, durationMinutes, bufferMinutes, price = 50m, currency = "PLN" }));

    Task<HttpResponseMessage> BookAsync(TestBusiness business, Guid serviceId, DateTimeOffset startsAt) =>
        api.CreateClient(TestUser.Client()).PostAsJsonAsync($"/api/public/businesses/{business.Slug}/bookings", new
        {
            serviceId,
            startsAt,
            clientName = "Cleo Client",
            clientEmail = "cleo@example.com",
        });

    async Task<PublicSlots> GetSlotsAsync(TestBusiness business, Guid serviceId) =>
        await TestBusiness.ReadAsync<PublicSlots>(await api.CreateClient()
            .GetAsync($"/api/public/businesses/{business.Slug}/slots?serviceId={serviceId}"));
}

public sealed record ServiceWithBuffer(Guid Id, string Name, int DurationMinutes, int BufferMinutes);
