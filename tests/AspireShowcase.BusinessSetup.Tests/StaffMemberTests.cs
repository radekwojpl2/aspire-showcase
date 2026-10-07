using AspireShowcase.BusinessSetup;
using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.SharedKernel;

namespace AspireShowcase.BusinessSetup.Tests;

/// <summary>The rules of user story MVP-11, on the StaffMember aggregate and its link to Business.</summary>
public class StaffMemberTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    static readonly BusinessId BusinessId = BusinessId.New();

    static WeeklyPeriod Period(DayOfWeek day, string opens, string closes) =>
        new(day, TimeOnly.Parse(opens), TimeOnly.Parse(closes));

    static WeeklyHours Hours(params WeeklyPeriod[] periods) => WeeklyHours.Create(periods);

    // Open Monday 9–17 and Saturday 10–14.
    static readonly WeeklyHours OpeningHours =
        Hours(Period(DayOfWeek.Monday, "09:00", "17:00"), Period(DayOfWeek.Saturday, "10:00", "14:00"));

    static Business OpenBusiness()
    {
        var business = Business.Start("Anna's Hair", "anna-hair", "Europe/Warsaw", "hello@anna-hair.example", "owner-1", Now);
        business.SetOpeningHours(OpeningHours, "Europe/Warsaw", []);
        return business;
    }

    static StaffMember Add(
        string? name, bool doesAllServices = true, IEnumerable<ServiceId>? serviceIds = null,
        IReadOnlyCollection<ServiceId>? businessServiceIds = null, WeeklyHours? workingHours = null) =>
        StaffMember.Add(
            BusinessId, name, doesAllServices, serviceIds, businessServiceIds ?? [], workingHours, OpeningHours, Now);

    static IReadOnlyDictionary<string, string[]> ErrorsOf(Action change) =>
        Assert.Throws<DomainValidationException>(change).Errors;

    [Fact]
    public void The_owner_is_a_staff_member_who_does_everything_whenever_the_business_is_open()
    {
        var owner = StaffMember.ForOwner(BusinessId, "owner-1", "Anna", Now);

        Assert.Equal("Anna", owner.Name);
        Assert.Equal("owner-1", owner.UserId);
        Assert.Equal(BusinessId, owner.BusinessId);
        Assert.True(owner.DoesAllServices);
        Assert.True(owner.Does(ServiceId.New()));
        Assert.Null(owner.WorkingHours);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void The_owner_is_called_Owner_until_named(string? name)
    {
        var owner = StaffMember.ForOwner(BusinessId, "owner-1", name, Now);

        Assert.Equal(StaffMember.DefaultOwnerName, owner.Name);
    }

    [Fact]
    public void A_staff_member_can_do_only_some_services()
    {
        var haircut = ServiceId.New();
        var colouring = ServiceId.New();

        var member = Add("Ben", false, [haircut], [haircut, colouring]);

        Assert.True(member.Does(haircut));
        Assert.False(member.Does(colouring));
    }

    [Fact]
    public void Someone_who_does_only_some_services_does_at_least_one()
    {
        var errors = ErrorsOf(() => Add("Ben", false, [], [ServiceId.New()]));

        Assert.True(errors.ContainsKey("serviceIds"));
    }

    [Fact]
    public void Services_have_to_belong_to_the_business()
    {
        var errors = ErrorsOf(() => Add("Ben", false, [ServiceId.New()], [ServiceId.New()]));

        Assert.True(errors.ContainsKey("serviceIds"));
    }

    [Fact]
    public void Working_hours_within_the_opening_hours_are_accepted()
    {
        var hours = Hours(Period(DayOfWeek.Monday, "09:00", "13:00"), Period(DayOfWeek.Saturday, "10:00", "14:00"));

        var member = Add("Ben", workingHours: hours);

        Assert.Equal(hours, member.WorkingHours);
    }

    [Fact]
    public void Working_hours_outside_the_opening_hours_are_refused_under_their_day()
    {
        var hours = Hours(Period(DayOfWeek.Monday, "16:00", "18:00"), Period(DayOfWeek.Tuesday, "09:00", "12:00"));

        var errors = ErrorsOf(() => Add("Ben", workingHours: hours));

        Assert.Equal(["Monday 16:00–18:00 is outside the opening hours."], errors["monday"]);
        Assert.Equal(["Tuesday 09:00–12:00 is outside the opening hours."], errors["tuesday"]);
    }

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        var errors = ErrorsOf(() => Add("", false, [], [], Hours(Period(DayOfWeek.Sunday, "10:00", "11:00"))));

        Assert.Equal(["name", "serviceIds", "sunday"], errors.Keys.Order());
    }

    [Fact]
    public void Opening_hours_cannot_shrink_past_the_working_hours_of_someone_on_the_staff()
    {
        var business = OpenBusiness();
        var saturdays = Hours(Period(DayOfWeek.Saturday, "10:00", "14:00"));

        var errors = ErrorsOf(() => business.SetOpeningHours(
            Hours(Period(DayOfWeek.Monday, "09:00", "17:00")), "Europe/Warsaw", [new StaffHours("Ben", saturdays)]));

        Assert.Contains("Ben works Saturday 10:00–14:00", Assert.Single(errors["staff"]));
        Assert.Equal(OpeningHours, business.OpeningHours);
    }

    [Fact]
    public void Staff_who_work_whenever_the_business_is_open_follow_any_change()
    {
        var business = OpenBusiness();
        var owner = StaffMember.ForOwner(business.Id, business.OwnerId, "Anna", Now);

        // The owner has no hours of their own, so there are none to check.
        business.SetOpeningHours(Hours(Period(DayOfWeek.Friday, "08:00", "12:00")), "Europe/Warsaw", []);

        Assert.Null(owner.WorkingHours);
    }

    [Fact]
    public void A_refused_change_leaves_the_staff_member_as_they_were()
    {
        var member = Add("Ben");

        ErrorsOf(() => member.Change(
            "Benjamin", true, null, [], Hours(Period(DayOfWeek.Sunday, "10:00", "11:00")), OpeningHours));

        Assert.Equal("Ben", member.Name);
        Assert.Null(member.WorkingHours);
    }
}
