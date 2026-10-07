using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.Scheduling;
using AspireShowcase.SharedKernel;

namespace AspireShowcase.Scheduling.Tests;

/// <summary>The rules of user story V1-4 on the Booking aggregate: moving a booking to another time.</summary>
public class RescheduleTests
{
    static readonly DateTimeOffset Start = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset Now = Start.AddDays(-3);
    static readonly StaffMemberId Anna = StaffMemberId.New();
    static readonly StaffMemberId Ben = StaffMemberId.New();

    // 45 minutes with a 15-minute buffer, under a 24-hour cancellation policy.
    static Booking Booked()
    {
        var booking = Booking.Book(
            BusinessId.New(), Anna, ServiceId.New(), Start, TimeSpan.FromMinutes(45), TimeSpan.FromMinutes(15),
            TimeSpan.FromHours(24), Attendee.Create("client-1", "Ola Nowak", "ola.nowak@example.com"), Start.AddDays(-7));
        booking.ClearEvents();
        return booking;
    }

    static IReadOnlyDictionary<string, string[]> ErrorsOf(Action change) =>
        Assert.Throws<DomainValidationException>(change).Errors;

    [Fact]
    public void A_moved_booking_keeps_its_length_and_buffer()
    {
        var booking = Booked();

        booking.Reschedule(Start.AddHours(2), Anna, Now, RescheduledBy.Client);

        Assert.Equal(Start.AddHours(2), booking.Start);
        Assert.Equal(Start.AddHours(2).AddMinutes(45), booking.End);
        Assert.Equal(Start.AddHours(2).AddMinutes(60), booking.OccupiedUntil);
    }

    [Fact]
    public void Moving_a_booking_records_where_it_was_and_who_moved_it()
    {
        var booking = Booked();

        booking.Reschedule(Start.AddDays(1), Ben, Now, RescheduledBy.Business);

        var moved = Assert.IsType<BookingRescheduled>(Assert.Single(booking.Events));
        Assert.Equal((Start, Anna, RescheduledBy.Business), (moved.PreviousStart, moved.PreviousStaffMemberId, moved.By));
        Assert.Equal(Ben, booking.StaffMemberId);
    }

    [Fact]
    public void Moving_a_booking_where_it_already_is_changes_nothing()
    {
        var booking = Booked();

        booking.Reschedule(Start, Anna, Now, RescheduledBy.Client);

        Assert.Empty(booking.Events);
    }

    [Fact]
    public void A_client_cannot_move_a_booking_inside_the_notice_period_but_the_business_can()
    {
        var booking = Booked();

        var errors = ErrorsOf(() => booking.Reschedule(Start.AddDays(1), Anna, Start.AddHours(-2), RescheduledBy.Client));
        booking.Reschedule(Start.AddDays(1), Anna, Start.AddHours(-2), RescheduledBy.Business);

        Assert.Contains("contact the business", errors["booking"][0]);
        Assert.Equal(Start.AddDays(1), booking.Start);
    }

    [Fact]
    public void A_cancelled_booking_cannot_be_moved()
    {
        var booking = Booked();
        booking.Cancel(Now, CancelledBy.Business);

        var errors = ErrorsOf(() => booking.Reschedule(Start.AddDays(1), Anna, Now, RescheduledBy.Business));

        Assert.True(errors.ContainsKey("booking"));
    }

    [Fact]
    public void A_booking_cannot_be_moved_once_it_has_started_or_to_a_time_that_has()
    {
        var booking = Booked();

        var started = ErrorsOf(() => booking.Reschedule(Start.AddDays(1), Anna, Start.AddMinutes(5), RescheduledBy.Business));
        var past = ErrorsOf(() => booking.Reschedule(Now.AddHours(-1), Anna, Now, RescheduledBy.Business));

        Assert.True(started.ContainsKey("booking"));
        Assert.True(past.ContainsKey("startsAt"));
    }
}
