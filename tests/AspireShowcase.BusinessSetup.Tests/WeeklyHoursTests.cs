using AspireShowcase.BusinessSetup;
using AspireShowcase.SharedKernel;

namespace AspireShowcase.BusinessSetup.Tests;

/// <summary>The rules of user story MVP-9, on the WeeklyHours value object and the Business aggregate.</summary>
public class WeeklyHoursTests
{
    static WeeklyPeriod Period(DayOfWeek day, string opens, string closes) =>
        new(day, TimeOnly.Parse(opens), TimeOnly.Parse(closes));

    static IReadOnlyDictionary<string, string[]> ErrorsOf(Action change) =>
        Assert.Throws<DomainValidationException>(change).Errors;

    [Fact]
    public void A_day_can_have_more_than_one_period()
    {
        var hours = WeeklyHours.Create([
            Period(DayOfWeek.Monday, "13:00", "17:00"),
            Period(DayOfWeek.Monday, "09:00", "12:00"),
        ]);

        Assert.Equal(
            [Period(DayOfWeek.Monday, "09:00", "12:00"), Period(DayOfWeek.Monday, "13:00", "17:00")],
            hours.Periods);
    }

    [Fact]
    public void A_day_without_periods_is_closed()
    {
        var hours = WeeklyHours.Create([Period(DayOfWeek.Monday, "09:00", "17:00")]);

        Assert.False(hours.Covers(DayOfWeek.Tuesday, TimeOnly.Parse("10:00"), TimeOnly.Parse("11:00")));
    }

    [Fact]
    public void Periods_can_touch()
    {
        var hours = WeeklyHours.Create([
            Period(DayOfWeek.Friday, "09:00", "12:00"),
            Period(DayOfWeek.Friday, "12:00", "15:00"),
        ]);

        Assert.Equal(2, hours.Periods.Count);
    }

    [Fact]
    public void Overlapping_periods_are_refused_under_their_day()
    {
        var errors = ErrorsOf(() => WeeklyHours.Create([
            Period(DayOfWeek.Monday, "09:00", "12:00"),
            Period(DayOfWeek.Monday, "11:00", "14:00"),
        ]));

        Assert.Equal(["09:00–12:00 and 11:00–14:00 overlap."], errors["monday"]);
    }

    [Fact]
    public void The_same_times_on_different_days_do_not_overlap()
    {
        var hours = WeeklyHours.Create([
            Period(DayOfWeek.Monday, "09:00", "12:00"),
            Period(DayOfWeek.Tuesday, "09:00", "12:00"),
        ]);

        Assert.Equal(2, hours.Periods.Count);
    }

    [Theory]
    [InlineData("12:00", "12:00")]
    [InlineData("17:00", "09:00")]
    public void Closing_has_to_be_after_opening(string opens, string closes)
    {
        var errors = ErrorsOf(() => WeeklyHours.Create([Period(DayOfWeek.Wednesday, opens, closes)]));

        Assert.Contains("closing has to be after opening", Assert.Single(errors["wednesday"]));
    }

    [Fact]
    public void Times_come_in_5_minute_steps()
    {
        var errors = ErrorsOf(() => WeeklyHours.Create([Period(DayOfWeek.Thursday, "09:03", "17:00")]));

        Assert.Contains("5-minute steps", Assert.Single(errors["thursday"]));
    }

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        var errors = ErrorsOf(() => WeeklyHours.Create([
            Period(DayOfWeek.Monday, "10:00", "09:00"),
            Period(DayOfWeek.Saturday, "09:01", "12:00"),
        ]));

        Assert.Equal(["monday", "saturday"], errors.Keys.Order());
    }

    [Fact]
    public void A_day_has_at_most_six_periods()
    {
        var periods = Enumerable.Range(0, WeeklyHours.MaxPeriodsPerDay + 1)
            .Select(hour => Period(DayOfWeek.Sunday, $"{8 + hour:00}:00", $"{8 + hour:00}:30"));

        var errors = ErrorsOf(() => WeeklyHours.Create(periods));

        Assert.Contains($"at most {WeeklyHours.MaxPeriodsPerDay}", errors["sunday"][0]);
    }

    [Theory]
    [InlineData("09:00", "10:00", true)]
    [InlineData("11:00", "12:00", true)]
    [InlineData("11:30", "13:30", false)] // spans the lunch break
    [InlineData("12:15", "12:45", false)] // during the lunch break
    [InlineData("16:30", "17:30", false)] // runs past closing
    public void An_appointment_fits_only_within_one_period(string start, string end, bool covered)
    {
        var hours = WeeklyHours.Create([
            Period(DayOfWeek.Monday, "09:00", "12:00"),
            Period(DayOfWeek.Monday, "13:00", "17:00"),
        ]);

        Assert.Equal(covered, hours.Covers(DayOfWeek.Monday, TimeOnly.Parse(start), TimeOnly.Parse(end)));
    }

    [Fact]
    public void Hours_with_the_same_periods_are_equal()
    {
        var first = WeeklyHours.Create([Period(DayOfWeek.Monday, "09:00", "17:00")]);
        var second = WeeklyHours.Create([Period(DayOfWeek.Monday, "09:00", "17:00")]);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void A_new_business_is_closed_until_its_hours_are_set()
    {
        var business = Business.Start("Anna's Hair", "anna-hair", "Europe/Warsaw", "user-1", DateTimeOffset.UtcNow);

        Assert.Equal(WeeklyHours.Closed, business.OpeningHours);
    }

    [Fact]
    public void Setting_hours_replaces_them_and_the_time_zone()
    {
        var business = Business.Start("Anna's Hair", "anna-hair", "Europe/Warsaw", "user-1", DateTimeOffset.UtcNow);
        var hours = WeeklyHours.Create([Period(DayOfWeek.Monday, "09:00", "17:00")]);

        business.SetOpeningHours(hours, "Europe/London", []);

        Assert.Equal(hours, business.OpeningHours);
        Assert.Equal("Europe/London", business.TimeZone);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("Central European Standard Time")] // a Windows ID, not IANA
    public void The_time_zone_has_to_be_an_IANA_time_zone(string? timeZone)
    {
        var business = Business.Start("Anna's Hair", "anna-hair", "Europe/Warsaw", "user-1", DateTimeOffset.UtcNow);

        var errors = ErrorsOf(() => business.SetOpeningHours(WeeklyHours.Closed, timeZone, []));

        Assert.True(errors.ContainsKey("timeZone"));
        Assert.Equal("Europe/Warsaw", business.TimeZone);
    }
}
