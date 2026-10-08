using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AspireShowcase.BusinessSetup.Infrastructure;

/// <summary>
/// How <see cref="WeeklyHours"/> are stored: the value object is always read and replaced as a
/// whole, so one jsonb column holds it: [{"day":"monday","opens":"09:00","closes":"12:00"}, ...]
/// </summary>
static class WeeklyHoursStorage
{
    static readonly ValueConverter<WeeklyHours, string> Converter = new(
        hours => Serialize(hours),
        json => Deserialize(json));

    static readonly ValueComparer<WeeklyHours> Comparer = new(
        (left, right) => Equals(left, right),
        hours => hours.GetHashCode(),
        // Immutable, so the snapshot can be the same instance.
        hours => hours);

    public static void StoredAsJson(this PropertyBuilder property) =>
        property.HasColumnType("jsonb").HasConversion(Converter, Comparer);

    sealed record StoredPeriod(string Day, string Opens, string Closes);

    static string Serialize(WeeklyHours hours) =>
        JsonSerializer.Serialize(
            hours.Periods.Select(period => new StoredPeriod(
                WeeklyHours.FieldName(period.Day),
                period.Opens.ToString("HH:mm", CultureInfo.InvariantCulture),
                period.Closes.ToString("HH:mm", CultureInfo.InvariantCulture))),
            JsonSerializerOptions.Web);

    static WeeklyHours Deserialize(string json) =>
        WeeklyHours.Restore(
            (JsonSerializer.Deserialize<List<StoredPeriod>>(json, JsonSerializerOptions.Web) ?? [])
                .Select(period => new WeeklyPeriod(
                    Enum.Parse<DayOfWeek>(period.Day, ignoreCase: true),
                    TimeOnly.ParseExact(period.Opens, "HH:mm", CultureInfo.InvariantCulture),
                    TimeOnly.ParseExact(period.Closes, "HH:mm", CultureInfo.InvariantCulture))));
}
