using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using AspireShowcase.Scheduling.PublicClient;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AspireShowcase.Notifications.Tests;

/// <summary>
/// What the consumer does with Resend's answers: an email Resend refuses for good is skipped,
/// and the booking's other emails still go out; anything else fails the message, so it's retried.
/// </summary>
public class BookingEmailsConsumerTests
{
    static BookingNotice Notice(string clientEmail) => new(
        "confirmed", null, Guid.NewGuid(), "Anna Hair", "anna-hair", "Haircut", "Ben Barber", "2026-11-03", "11:00", "12:00",
        "Europe/Warsaw", new BookingParty("Cleo Client", clientEmail), new BookingParty("Olivia Owner", "olivia@salon.test"));

    [Fact]
    public async Task An_email_Resend_refuses_is_skipped_and_the_others_still_go_out()
    {
        // Like Resend: no example.com addresses.
        var resend = new StubResend(to => to.EndsWith("@example.com") ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.OK);

        var consumed = await ConsumeAsync(resend, Notice("cleo@example.com"));

        Assert.Null(consumed.Exception);
        Assert.Equal(["cleo@example.com", "olivia@salon.test"], resend.Requests);
        Assert.Equal(["olivia@salon.test"], resend.Accepted);
    }

    [Fact]
    public async Task An_email_Resend_cannot_take_now_fails_the_message_so_it_is_retried()
    {
        var resend = new StubResend(_ => HttpStatusCode.ServiceUnavailable);

        var consumed = await ConsumeAsync(resend, Notice("cleo@salon.test"));

        Assert.NotNull(consumed.Exception);
    }

    static async Task<IReceivedMessage<BookingNotice>> ConsumeAsync(StubResend resend, BookingNotice notice)
    {
        var settings = new EmailSettings { From = "Bookings <bookings@salon.test>", ResendApiKey = "test-key", AppUrl = "https://app.test" };
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddMetrics()
            .AddSingleton(settings)
            .AddSingleton(new ResendEmailSender(new HttpClient(resend) { BaseAddress = new Uri("https://resend.test/") }, settings))
            .AddSingleton<NotificationStore>()
            .AddSingleton<PendingDigest>()
            .AddSingleton<NotificationTelemetry>()
            // The consumer alone, without its inbox in notifications-db.
            .AddMassTransitTestHarness(bus => bus.AddConsumer<BookingEmailsConsumer>())
            .BuildServiceProvider(true);

        var harness = services.GetRequiredService<ITestHarness>();
        await harness.Start();
        await harness.Bus.Publish(notice);
        Assert.True(await harness.Consumed.Any<BookingNotice>());
        return harness.Consumed.Select<BookingNotice>().Single();
    }

    /// <summary>Resend's send-email endpoint, answering each recipient as told.</summary>
    sealed class StubResend(Func<string, HttpStatusCode> answer) : HttpMessageHandler
    {
        readonly ConcurrentQueue<string> _requests = new();
        readonly ConcurrentQueue<string> _accepted = new();

        public string[] Requests => [.. _requests];

        public string[] Accepted => [.. _accepted];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var to = (await request.Content!.ReadFromJsonAsync<SentEmail>(cancellationToken))!.To.Single();
            _requests.Enqueue(to);
            var status = answer(to);
            if (status == HttpStatusCode.OK)
            {
                _accepted.Enqueue(to);
            }
            return new HttpResponseMessage(status) { Content = new StringContent("""{"message":"refused by the stub"}""") };
        }

        sealed record SentEmail(string[] To);
    }
}
