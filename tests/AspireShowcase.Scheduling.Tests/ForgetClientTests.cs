using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.Scheduling.Domain;

namespace AspireShowcase.Scheduling.Tests;

/// <summary>
/// A client deleted their account (V1-8): their bookings stay for the business, without their
/// name, email or account, and the upcoming ones are cancelled.
/// </summary>
public class ForgetClientTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    // Booked a week earlier, under a 24-hour cancellation notice.
    static Booking BookingAt(DateTimeOffset start) => Booking.Book(
        BusinessId.New(), StaffMemberId.New(), ServiceId.New(), start, TimeSpan.FromMinutes(30), TimeSpan.Zero,
        TimeSpan.FromHours(24), Attendee.Create("client-1", "Ola Nowak", "ola.nowak@example.com"), Now.AddDays(-7));

    [Fact]
    public void An_upcoming_booking_is_cancelled_and_kept_without_the_client()
    {
        var booking = BookingAt(Now.AddDays(3));
        booking.ClearEvents();

        booking.ForgetClient(Now);

        Assert.Equal((BookingStatus.Cancelled, CancelledBy.Client), (booking.Status, booking.CancelledBy));
        Assert.Equal(Attendee.Forgotten, booking.Attendee);
        Assert.IsType<BookingCancelled>(Assert.Single(booking.Events));
    }

    [Fact]
    public void An_upcoming_booking_is_cancelled_even_inside_the_cancellation_notice()
    {
        var booking = BookingAt(Now.AddHours(2));

        booking.ForgetClient(Now);

        Assert.Equal(BookingStatus.Cancelled, booking.Status);
    }

    [Fact]
    public void A_past_booking_stays_for_the_business_without_the_name_and_email_of_the_client()
    {
        var booking = BookingAt(Now.AddDays(-1));
        booking.ClearEvents();

        booking.ForgetClient(Now);

        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal((null, "A former client", null), (booking.Attendee.UserId, booking.Attendee.Name, booking.Attendee.Email));
        Assert.Empty(booking.Events);
    }

    [Fact]
    public void A_cancelled_booking_loses_the_client_but_is_not_cancelled_again()
    {
        var booking = BookingAt(Now.AddDays(3));
        booking.Cancel(Now.AddDays(-1), CancelledBy.Business);
        booking.ClearEvents();

        booking.ForgetClient(Now);

        Assert.Equal((CancelledBy.Business, Attendee.Forgotten), (booking.CancelledBy, booking.Attendee));
        Assert.Empty(booking.Events);
    }

    [Fact]
    public void Forgetting_the_client_twice_changes_nothing_more()
    {
        var booking = BookingAt(Now.AddDays(3));
        booking.ForgetClient(Now);
        booking.ClearEvents();

        booking.ForgetClient(Now);

        Assert.Equal((BookingStatus.Cancelled, Attendee.Forgotten), (booking.Status, booking.Attendee));
        Assert.Empty(booking.Events);
    }
}
