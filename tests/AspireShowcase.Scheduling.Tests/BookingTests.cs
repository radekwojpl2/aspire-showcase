using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.Scheduling;
using AspireShowcase.SharedKernel;

namespace AspireShowcase.Scheduling.Tests;

/// <summary>
/// Scheduling's domain rules: bookings and their attendee, and the days a calendar view covers
/// (user story MVP-12). The no-overlap rule is the database's, so it isn't tested here.
/// </summary>
public class BookingTests
{
    static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    [Fact]
    public void A_booking_lasts_as_long_as_the_service_and_is_stored_in_UTC()
    {
        var start = new DateTimeOffset(2026, 10, 6, 9, 0, 0, TimeSpan.FromHours(2));

        var booking = Booking.Book(
            BusinessId.New(), StaffMemberId.New(), ServiceId.New(), start, TimeSpan.FromMinutes(45),
            Attendee.Create(null, "Ola Nowak", "ola.nowak@example.com"), DateTimeOffset.UtcNow);

        Assert.Equal(new DateTimeOffset(2026, 10, 6, 7, 0, 0, TimeSpan.Zero), booking.Start);
        Assert.Equal(TimeSpan.Zero, booking.Start.Offset);
        Assert.Equal(TimeSpan.FromMinutes(45), booking.End - booking.Start);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
    }

    [Fact]
    public void A_booking_needs_a_duration()
    {
        Assert.Throws<DomainValidationException>(() => Booking.Book(
            BusinessId.New(), StaffMemberId.New(), ServiceId.New(), DateTimeOffset.UtcNow, TimeSpan.Zero,
            Attendee.Create(null, "Ola Nowak", "ola.nowak@example.com"), DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("", "ola.nowak@example.com", "clientName")]
    [InlineData("Ola Nowak", "not an email", "clientEmail")]
    [InlineData("Ola Nowak", null, "clientEmail")]
    public void An_attendee_has_a_name_and_an_email(string? name, string? email, string field)
    {
        var errors = Assert.Throws<DomainValidationException>(() => Attendee.Create(null, name, email)).Errors;

        Assert.True(errors.ContainsKey(field));
    }

    [Fact]
    public void A_week_runs_from_Monday_to_Sunday()
    {
        // Thursday 8 October 2026.
        var range = CalendarRange.For(new DateOnly(2026, 10, 8), CalendarView.Week, Warsaw);

        Assert.Equal(new DateOnly(2026, 10, 5), range.FirstDay);
        Assert.Equal(new DateOnly(2026, 10, 11), range.LastDay);
    }

    [Fact]
    public void A_week_asked_for_on_Sunday_is_the_week_that_ends_then()
    {
        var range = CalendarRange.For(new DateOnly(2026, 10, 11), CalendarView.Week, Warsaw);

        Assert.Equal(new DateOnly(2026, 10, 5), range.FirstDay);
    }

    [Fact]
    public void A_day_spans_local_midnight_to_midnight()
    {
        var range = CalendarRange.For(new DateOnly(2026, 10, 6), CalendarView.Day, Warsaw);

        // Warsaw is UTC+2 in summer time.
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 22, 0, 0, TimeSpan.Zero), range.From);
        Assert.Equal(TimeSpan.FromHours(24), range.To - range.From);
    }

    [Fact]
    public void The_day_the_clocks_go_back_has_25_hours()
    {
        // Summer time ends in Europe on Sunday 25 October 2026.
        var range = CalendarRange.For(new DateOnly(2026, 10, 25), CalendarView.Day, Warsaw);

        Assert.Equal(TimeSpan.FromHours(25), range.To - range.From);
    }
}
