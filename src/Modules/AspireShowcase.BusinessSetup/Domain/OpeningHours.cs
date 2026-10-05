using AspireShowcase.SharedKernel;

namespace AspireShowcase.BusinessSetup;

/// <summary>One stretch of a weekday when the business is open, in its local time: [Opens, Closes).</summary>
sealed record OpeningPeriod(DayOfWeek Day, TimeOnly Opens, TimeOnly Closes);

/// <summary>
/// A business's weekly opening hours: any number of periods per weekday, such as 9–12 and 13–17,
/// in the business's local time. A day without periods is closed.
/// </summary>
/// <remarks>
/// A value object: it's replaced as a whole, never changed, and two are equal when their periods
/// are. Periods end within their day, so a business can't be open past midnight for now.
/// </remarks>
sealed class OpeningHours : IEquatable<OpeningHours>
{
    /// <summary>Times come in 5-minute steps, like service durations.</summary>
    public const int StepMinutes = 5;

    public const int MaxPeriodsPerDay = 6;

    public static readonly OpeningHours Closed = new([]);

    OpeningHours(IReadOnlyList<OpeningPeriod> periods) => Periods = periods;

    /// <summary>The periods, by weekday (Sunday first, as <see cref="DayOfWeek"/> counts) and opening time.</summary>
    public IReadOnlyList<OpeningPeriod> Periods { get; }

    /// <summary>
    /// Checks the periods and puts them in order. Errors are reported per weekday, under its
    /// lowercase English name ("monday"), so the UI can show them next to the right day.
    /// </summary>
    /// <exception cref="DomainValidationException">A period is empty, off the 5-minute steps, or
    /// overlaps another on the same day, or a day has too many periods.</exception>
    public static OpeningHours Create(IEnumerable<OpeningPeriod> periods)
    {
        var sorted = periods.OrderBy(period => period.Day).ThenBy(period => period.Opens).ToList();
        var errors = new DomainErrors();

        foreach (var day in sorted.GroupBy(period => period.Day))
        {
            var field = FieldName(day.Key);
            if (day.Count() > MaxPeriodsPerDay)
            {
                errors.Add(field, $"Use at most {MaxPeriodsPerDay} periods a day.");
            }

            OpeningPeriod? previous = null;
            foreach (var period in day)
            {
                if (!IsOnStep(period.Opens) || !IsOnStep(period.Closes))
                {
                    errors.Add(field, $"{Format(period)}: use times in {StepMinutes}-minute steps.");
                }
                else if (period.Closes <= period.Opens)
                {
                    errors.Add(field, $"{Format(period)}: closing has to be after opening.");
                }
                else if (previous is not null && period.Opens < previous.Closes)
                {
                    errors.Add(field, $"{Format(previous)} and {Format(period)} overlap.");
                }
                previous = period;
            }
        }

        errors.ThrowIfAny();
        return new OpeningHours(sorted);
    }

    /// <summary>For hours that were checked when they were stored.</summary>
    public static OpeningHours Restore(IEnumerable<OpeningPeriod> periods) => new(periods.ToList());

    /// <summary>
    /// Whether an appointment from <paramref name="start"/> to <paramref name="end"/> on that day
    /// fits within one opening period. Scheduling's availability will ask this.
    /// </summary>
    public bool Covers(DayOfWeek day, TimeOnly start, TimeOnly end) =>
        start < end && Periods.Any(period => period.Day == day && period.Opens <= start && end <= period.Closes);

    public static string FieldName(DayOfWeek day) => day.ToString().ToLowerInvariant();

    static bool IsOnStep(TimeOnly time) => time.Second == 0 && time.Millisecond == 0 && time.Minute % StepMinutes == 0;

    static string Format(OpeningPeriod period) => $"{period.Opens:HH\\:mm}–{period.Closes:HH\\:mm}";

    public bool Equals(OpeningHours? other) => other is not null && Periods.SequenceEqual(other.Periods);

    public override bool Equals(object? obj) => Equals(obj as OpeningHours);

    public override int GetHashCode() =>
        Periods.Aggregate(0, (hash, period) => HashCode.Combine(hash, period));
}
