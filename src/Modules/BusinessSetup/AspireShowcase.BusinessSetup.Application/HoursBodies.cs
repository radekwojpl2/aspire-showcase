using System.Globalization;
using AspireShowcase.BuildingBlocks.Domain;

namespace AspireShowcase.BusinessSetup.Application;

/// <summary>Weekly hours as the API takes and gives them, for opening hours and working hours alike.</summary>
static class HoursBodies
{
    /// <summary>
    /// Turns the request into weekly hours. Malformed days and times are reported the same way as
    /// the domain's own rules, under the day they belong to.
    /// </summary>
    /// <exception cref="DomainValidationException">A day or time can't be read, or the hours break a rule.</exception>
    public static WeeklyHours Parse(List<OpeningPeriodBody>? periods)
    {
        var errors = new DomainErrors();
        var parsed = new List<WeeklyPeriod>();
        foreach (var period in periods ?? [])
        {
            // Exact names only: Enum.TryParse would also take "1" or "monday,tuesday".
            if (Enum.GetValues<DayOfWeek>().Cast<DayOfWeek?>()
                    .FirstOrDefault(weekday => WeeklyHours.FieldName(weekday!.Value) == period.Day) is not { } day)
            {
                errors.Add("periods", $"\"{period.Day}\" isn't a weekday.");
                continue;
            }
            if (!TryParseTime(period.Opens, out var opens) || !TryParseTime(period.Closes, out var closes))
            {
                errors.Add(WeeklyHours.FieldName(day), "Enter both times as HH:mm.");
                continue;
            }
            parsed.Add(new WeeklyPeriod(day, opens, closes));
        }
        errors.ThrowIfAny();
        return WeeklyHours.Create(parsed);
    }

    public static List<OpeningPeriodBody> ToBodies(WeeklyHours hours) =>
        hours.Periods
            .Select(period => new OpeningPeriodBody(
                WeeklyHours.FieldName(period.Day),
                period.Opens.ToString("HH:mm", CultureInfo.InvariantCulture),
                period.Closes.ToString("HH:mm", CultureInfo.InvariantCulture)))
            .ToList();

    static bool TryParseTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}
