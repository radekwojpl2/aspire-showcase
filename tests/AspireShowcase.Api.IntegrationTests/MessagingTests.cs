using AspireShowcase.Scheduling.PublicClient;
using MassTransit;
using MassTransit.Testing;

namespace AspireShowcase.Api.IntegrationTests;

/// <summary>
/// What Scheduling publishes: booking events through the transactional outbox in PostgreSQL, and
/// the notice the notifications service turns into emails.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class MessagingTests(ApiFactory api)
{
    [Fact]
    public async Task Booking_and_cancelling_publish_their_events()
    {
        var business = await TestBusiness.StartAsync(api);
        var client = TestUser.Client();

        var booked = await TestBusiness.ReadAsync<BookingConfirmation>(
            await business.BookAsync(api, client, TestBusiness.FirstSlot));
        await api.CreateClient(client).PostAsync($"/api/me/bookings/{booked.Id}/cancel", null);

        var confirmed = await ConsumedAsync<BookingConfirmed>(message => message.BookingId == booked.Id);
        var cancelled = await ConsumedAsync<BookingCancelled>(message => message.BookingId == booked.Id);
        Assert.Equal(business.Id, confirmed.BusinessId);
        Assert.Equal("client", cancelled.CancelledBy);
    }

    [Fact]
    public async Task A_booking_notice_has_what_the_emails_need()
    {
        var business = await TestBusiness.StartAsync(api);

        var booked = await TestBusiness.ReadAsync<BookingConfirmation>(
            await business.BookAsync(api, TestUser.Client(), TestBusiness.FirstSlot));

        var notice = await ConsumedAsync<BookingNotice>(message => message.BookingId == booked.Id);
        Assert.Equal(
            ("confirmed", "Haircut", "2026-11-02", "09:00", TestBusiness.TimeZone),
            (notice.Kind, notice.ServiceName, notice.Date, notice.Start, notice.TimeZone));
        Assert.Equal(new BookingParty("Cleo Client", "cleo@example.com"), notice.Client);
        // The owner's email comes from Logto, through Identity.
        Assert.Equal(FakeUserProfiles.EmailOf(business.Owner.Id), notice.Owner.Email);
    }

    /// <summary>
    /// Waits for a message to reach its consumer (for <see cref="BookingNotice"/>, the
    /// <see cref="NotificationsStandIn"/>). The outbox sends on its own schedule, and the harness's
    /// own waits end as soon as the bus is idle, which it is between the outbox's polls.
    /// </summary>
    async Task<T> ConsumedAsync<T>(Func<T, bool> match) where T : class
    {
        var harness = api.Services.GetTestHarness();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            if (harness.Consumed.Select<T>().FirstOrDefault(received => match(received.Context.Message)) is { } found)
            {
                return found.Context.Message;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
        }
    }
}

/// <summary>Takes the notifications service's place on the bus, so booking notices have somewhere to go.</summary>
public sealed class NotificationsStandIn : IConsumer<BookingNotice>
{
    public Task Consume(ConsumeContext<BookingNotice> context) => Task.CompletedTask;
}
