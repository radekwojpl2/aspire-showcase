using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.Scheduling;
using AspireShowcase.BuildingBlocks.Domain;

namespace AspireShowcase.Scheduling.Tests;

/// <summary>The rules of user story V1-3: the cancellation policy, and the notice a booking keeps.</summary>
public class CancellationPolicyTests
{
    static readonly DateTimeOffset Start = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    static Booking BookedUnder(int noticeHours) => Booking.Book(
        BusinessId.New(), StaffMemberId.New(), ServiceId.New(), Start, TimeSpan.FromMinutes(30), TimeSpan.Zero,
        TimeSpan.FromHours(noticeHours), Attendee.Create("client-1", "Ola Nowak", "ola.nowak@example.com"), Start.AddDays(-7));

    [Fact]
    public void A_business_without_a_policy_has_no_notice()
    {
        Assert.Equal(TimeSpan.Zero, CancellationPolicy.None(BusinessId.New()).Notice);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(24)]
    [InlineData(CancellationPolicy.MaxNoticeHours)]
    public void The_notice_is_whole_hours_up_to_a_week(int hours)
    {
        var policy = CancellationPolicy.None(BusinessId.New());

        policy.Change(hours, Start);

        Assert.Equal(TimeSpan.FromHours(hours), policy.Notice);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(CancellationPolicy.MaxNoticeHours + 1)]
    public void Other_notices_are_refused(int? hours)
    {
        var errors = Assert.Throws<DomainValidationException>(
            () => CancellationPolicy.None(BusinessId.New()).Change(hours, Start)).Errors;

        Assert.True(errors.ContainsKey("noticeHours"));
    }

    [Fact]
    public void A_booking_keeps_the_notice_it_was_made_under()
    {
        var booking = BookedUnder(24);

        Assert.Equal(TimeSpan.FromHours(24), booking.ChangeNotice);
        Assert.Equal(Start.AddHours(-24), booking.ClientCanChangeUntil);
    }

    [Fact]
    public void A_client_can_cancel_until_the_notice_period_begins()
    {
        var booking = BookedUnder(24);

        booking.Cancel(Start.AddHours(-24), CancelledBy.Client);

        Assert.Equal(BookingStatus.Cancelled, booking.Status);
    }

    [Fact]
    public void A_client_cannot_cancel_inside_the_notice_period()
    {
        var booking = BookedUnder(24);

        var errors = Assert.Throws<DomainValidationException>(
            () => booking.Cancel(Start.AddHours(-23), CancelledBy.Client)).Errors;

        Assert.Contains("contact the business", errors["booking"][0]);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
    }

    [Fact]
    public void The_business_can_cancel_inside_the_notice_period()
    {
        var booking = BookedUnder(24);

        booking.Cancel(Start.AddHours(-1), CancelledBy.Business);

        Assert.Equal(BookingStatus.Cancelled, booking.Status);
    }
}
