using System.Globalization;
using ModelContextProtocol;

namespace AspireShowcase.Mcp.Tools;

/// <summary>
/// Free times as the tools show them and ask about them: a window of days, the choices the
/// user picks from, and times in the business's time zone.
/// </summary>
static class FreeTimes
{
    // As many free times as a form can offer in one list.
    const int MaxChoices = 20;

    /// <summary>
    /// The days of the calendar from <paramref name="from"/>, or today in the business's time zone,
    /// up to <paramref name="days"/> of them. Days without a free time aren't in web's answer, so
    /// they're counted by date, not by the days listed.
    /// </summary>
    public static IReadOnlyList<PublicDay> Window(PublicSlots slots, string? from, int days, TimeProvider time)
    {
        DateOnly first;
        if (from is null)
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(slots.TimeZone);
            first = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), timeZone).DateTime);
        }
        else if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out first))
        {
            throw new McpException("from must be a date, yyyy-MM-dd.");
        }

        var end = first.AddDays(Math.Clamp(days, 1, 28));
        return slots.Days
            .Where(day => DateOnly.ParseExact(day.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture) is var date
                && date >= first && date < end)
            .ToList();
    }

    /// <summary>The next free times, as choices: the instant to book, shown as the local day and time.</summary>
    public static IEnumerable<(string Value, string Title)> Choices(PublicSlots slots)
    {
        var choices = slots.Days
            .SelectMany(day => day.Slots.Select(slot => (slot.StartsAt.ToString("O", CultureInfo.InvariantCulture), $"{day.Date} {slot.Start}")))
            .Take(MaxChoices)
            .ToList();
        return choices.Count > 0 ? choices : throw new McpException("There are no free times to choose from.");
    }

    /// <summary>The time the user picked, if they were asked to.</summary>
    public static DateTimeOffset? Picked(Answer? answer) =>
        DateTimeOffset.TryParse(answer?.Text("startsAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var picked)
            ? picked
            : null;

    public static string Local(DateTimeOffset at, string timeZone) =>
        TimeZoneInfo.ConvertTime(at, TimeZoneInfo.FindSystemTimeZoneById(timeZone)).ToString("yyyy-MM-dd HH:mm");
}
