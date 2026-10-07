using AspireShowcase.Scheduling.PublicClient;

namespace AspireShowcase.Notifications.Tests;

/// <summary>
/// Which emails a booking notice sends, to whom, and what they say (MVP-5, MVP-7, MVP-13, MVP-14
/// and V1-4).
/// </summary>
public class BookingEmailsTests
{
    const string AppUrl = "https://app.example";

    static BookingNotice Notice(string kind, string? cancelledBy = null, string? ownerEmail = "olivia@salon.example") => new(
        kind, cancelledBy, Guid.NewGuid(), "Anna Hair", "anna-hair", "Haircut", "Ben Barber", "2026-11-03", "11:00", "12:00",
        "Europe/Warsaw", new BookingParty("Cleo Client", "cleo@example.com"), new BookingParty("Olivia Owner", ownerEmail));

    static (string Recipient, string Kind)[] Sent(BookingNotice notice) =>
        BookingEmails.For(notice, AppUrl).Select(email => (email.Recipient, email.Kind)).ToArray();

    [Fact]
    public void A_booking_confirms_to_the_client_and_tells_the_owner()
    {
        Assert.Equal([("client", "confirmation"), ("owner", "new-booking")], Sent(Notice("confirmed")));
    }

    [Fact]
    public void A_client_cancelling_tells_them_and_the_owner()
    {
        Assert.Equal(
            [("client", "cancelled-by-client"), ("owner", "cancelled-by-client")],
            Sent(Notice("cancelled", cancelledBy: "client")));
    }

    [Fact]
    public void The_business_cancelling_tells_only_the_client()
    {
        Assert.Equal([("client", "cancelled-by-business")], Sent(Notice("cancelled", cancelledBy: "business")));
    }

    [Fact]
    public void A_client_moving_a_booking_tells_them_and_the_owner_where_it_was_and_is()
    {
        var notice = Notice("rescheduled") with { RescheduledBy = "client", PreviousDate = "2026-11-02", PreviousStart = "09:00" };

        var emails = BookingEmails.For(notice, AppUrl).ToList();

        Assert.Equal([("client", "rescheduled-by-client"), ("owner", "rescheduled-by-client")],
            emails.Select(email => (email.Recipient, email.Kind)));
        Assert.All(emails, email => Assert.Contains("Monday 2 November, 09:00", email.Message.Text));
        Assert.All(emails, email => Assert.Contains("Tuesday 3 November, 11:00", email.Message.Text));
    }

    [Fact]
    public void The_business_moving_a_booking_tells_only_the_client()
    {
        var notice = Notice("rescheduled") with { RescheduledBy = "business", PreviousDate = "2026-11-02", PreviousStart = "09:00" };

        var email = Assert.Single(BookingEmails.For(notice, AppUrl));

        Assert.Equal(("client", "rescheduled-by-business"), (email.Recipient, email.Kind));
        Assert.StartsWith("Anna Hair moved your booking", email.Message.Subject);
    }

    [Fact]
    public void Someone_without_an_email_address_gets_no_email()
    {
        Assert.Equal([("client", "confirmation")], Sent(Notice("confirmed", ownerEmail: null)));
    }

    [Fact]
    public void Names_typed_by_users_are_encoded_in_the_html()
    {
        var notice = Notice("confirmed") with { Client = new BookingParty("<b>Cleo</b>", "cleo@example.com") };

        var email = BookingEmails.For(notice, AppUrl).First();

        Assert.Contains("&lt;b&gt;Cleo&lt;/b&gt;", email.Message.Html);
        Assert.DoesNotContain("<b>Cleo</b>", email.Message.Html);
    }
}
