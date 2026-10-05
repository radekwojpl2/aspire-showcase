using AspireShowcase.BusinessSetup;
using AspireShowcase.SharedKernel;

namespace AspireShowcase.BusinessSetup.Tests;

/// <summary>The rules of user story MVP-11, on the StaffMember aggregate and its link to Business.</summary>
public class StaffMemberTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    static WeeklyPeriod Period(DayOfWeek day, string opens, string closes) =>
        new(day, TimeOnly.Parse(opens), TimeOnly.Parse(closes));

    static WeeklyHours Hours(params WeeklyPeriod[] periods) => WeeklyHours.Create(periods);

    // Open Monday 9–17 and Saturday 10–14.
    static Business OpenBusiness()
    {
        var business = Business.Start("Anna's Hair", "anna-hair", "Europe/Warsaw", "owner-1", Now);
        business.SetOpeningHours(
            Hours(Period(DayOfWeek.Monday, "09:00", "17:00"), Period(DayOfWeek.Saturday, "10:00", "14:00")),
            "Europe/Warsaw", []);
        return business;
    }

    static IReadOnlyDictionary<string, string[]> ErrorsOf(Action change) =>
        Assert.Throws<DomainValidationException>(change).Errors;

    [Fact]
    public void The_owner_is_a_staff_member_who_does_everything_whenever_the_business_is_open()
    {
        var business = OpenBusiness();

        var owner = StaffMember.ForOwner(business, "Anna", Now);

        Assert.Equal("Anna", owner.Name);
        Assert.Equal(business.OwnerId, owner.UserId);
        Assert.True(owner.DoesAllServices);
        Assert.True(owner.Does(Guid.NewGuid()));
        Assert.Null(owner.WorkingHours);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void The_owner_is_called_Owner_until_named(string? name)
    {
        var owner = StaffMember.ForOwner(OpenBusiness(), name, Now);

        Assert.Equal(StaffMember.DefaultOwnerName, owner.Name);
    }

    [Fact]
    public void A_staff_member_can_do_only_some_services()
    {
        var haircut = Guid.NewGuid();
        var colouring = Guid.NewGuid();

        var member = StaffMember.Add(OpenBusiness(), "Ben", false, [haircut], [haircut, colouring], null, Now);

        Assert.True(member.Does(haircut));
        Assert.False(member.Does(colouring));
    }

    [Fact]
    public void Someone_who_does_only_some_services_does_at_least_one()
    {
        var errors = ErrorsOf(() => StaffMember.Add(OpenBusiness(), "Ben", false, [], [Guid.NewGuid()], null, Now));

        Assert.True(errors.ContainsKey("serviceIds"));
    }

    [Fact]
    public void Services_have_to_be_the_business_s_own()
    {
        var errors = ErrorsOf(() =>
            StaffMember.Add(OpenBusiness(), "Ben", false, [Guid.NewGuid()], [Guid.NewGuid()], null, Now));

        Assert.True(errors.ContainsKey("serviceIds"));
    }

    [Fact]
    public void Working_hours_within_the_opening_hours_are_accepted()
    {
        var hours = Hours(Period(DayOfWeek.Monday, "09:00", "13:00"), Period(DayOfWeek.Saturday, "10:00", "14:00"));

        var member = StaffMember.Add(OpenBusiness(), "Ben", true, null, [], hours, Now);

        Assert.Equal(hours, member.WorkingHours);
    }

    [Fact]
    public void Working_hours_outside_the_opening_hours_are_refused_under_their_day()
    {
        var hours = Hours(Period(DayOfWeek.Monday, "16:00", "18:00"), Period(DayOfWeek.Tuesday, "09:00", "12:00"));

        var errors = ErrorsOf(() => StaffMember.Add(OpenBusiness(), "Ben", true, null, [], hours, Now));

        Assert.Equal(["Monday 16:00–18:00 is outside the opening hours."], errors["monday"]);
        Assert.Equal(["Tuesday 09:00–12:00 is outside the opening hours."], errors["tuesday"]);
    }

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        var errors = ErrorsOf(() => StaffMember.Add(
            OpenBusiness(), "", false, [], [], Hours(Period(DayOfWeek.Sunday, "10:00", "11:00")), Now));

        Assert.Equal(["name", "serviceIds", "sunday"], errors.Keys.Order());
    }

    [Fact]
    public void Opening_hours_cant_shrink_past_someone_s_working_hours()
    {
        var business = OpenBusiness();
        var ben = StaffMember.Add(
            business, "Ben", true, null, [], Hours(Period(DayOfWeek.Saturday, "10:00", "14:00")), Now);

        var errors = ErrorsOf(() => business.SetOpeningHours(
            Hours(Period(DayOfWeek.Monday, "09:00", "17:00")), "Europe/Warsaw", [ben]));

        Assert.Contains("Ben works Saturday 10:00–14:00", Assert.Single(errors["staff"]));
        Assert.True(business.OpeningHours.Covers(DayOfWeek.Saturday, TimeOnly.Parse("10:00"), TimeOnly.Parse("14:00")));
    }

    [Fact]
    public void Staff_who_work_whenever_the_business_is_open_follow_any_change()
    {
        var business = OpenBusiness();
        var owner = StaffMember.ForOwner(business, "Anna", Now);

        business.SetOpeningHours(Hours(Period(DayOfWeek.Friday, "08:00", "12:00")), "Europe/Warsaw", [owner]);

        Assert.Null(owner.WorkingHours);
    }

    [Fact]
    public void A_refused_change_leaves_the_staff_member_as_they_were()
    {
        var business = OpenBusiness();
        var member = StaffMember.Add(business, "Ben", true, null, [], null, Now);

        ErrorsOf(() => member.Change(
            business, "Benjamin", true, null, [], Hours(Period(DayOfWeek.Sunday, "10:00", "11:00"))));

        Assert.Equal("Ben", member.Name);
        Assert.Null(member.WorkingHours);
    }
}
