using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AspireShowcase.Api.BusinessSetup;

/// <summary>How the <see cref="Business"/> aggregate is stored in app-db.</summary>
sealed class BusinessConfiguration : IEntityTypeConfiguration<Business>
{
    // Named so a unique violation can tell which rule was broken.
    public const string SlugIndex = "IX_Businesses_Slug";
    public const string OwnerIndex = "IX_Businesses_OwnerId";

    public void Configure(EntityTypeBuilder<Business> business)
    {
        business.ToTable("Businesses");
        business.Property(b => b.Name).HasMaxLength(Business.MaxNameLength);
        business.Property(b => b.Slug).HasMaxLength(BookingSlug.MaxLength);
        business.Property(b => b.OwnerId).HasMaxLength(64);
        business.Property(b => b.TimeZone).HasMaxLength(BusinessTimeZone.MaxLength);

        // The value object is always read and replaced as a whole, so one jsonb column holds it:
        // [{"day":"monday","opens":"09:00","closes":"12:00"}, ...]
        business.Property(b => b.OpeningHours)
            .HasColumnType("jsonb")
            .HasConversion(
                hours => Serialize(hours),
                json => Deserialize(json),
                new ValueComparer<OpeningHours>(
                    (left, right) => Equals(left, right),
                    hours => hours.GetHashCode(),
                    // Immutable, so the snapshot can be the same instance.
                    hours => hours));

        // The database is what guarantees both, even when two requests race.
        business.HasIndex(b => b.Slug).IsUnique().HasDatabaseName(SlugIndex);
        business.HasIndex(b => b.OwnerId).IsUnique().HasDatabaseName(OwnerIndex);
    }

    sealed record StoredPeriod(string Day, string Opens, string Closes);

    static string Serialize(OpeningHours hours) =>
        JsonSerializer.Serialize(
            hours.Periods.Select(period => new StoredPeriod(
                OpeningHours.FieldName(period.Day),
                period.Opens.ToString("HH:mm", CultureInfo.InvariantCulture),
                period.Closes.ToString("HH:mm", CultureInfo.InvariantCulture))),
            JsonSerializerOptions.Web);

    static OpeningHours Deserialize(string json) =>
        OpeningHours.Restore(
            (JsonSerializer.Deserialize<List<StoredPeriod>>(json, JsonSerializerOptions.Web) ?? [])
                .Select(period => new OpeningPeriod(
                    Enum.Parse<DayOfWeek>(period.Day, ignoreCase: true),
                    TimeOnly.ParseExact(period.Opens, "HH:mm", CultureInfo.InvariantCulture),
                    TimeOnly.ParseExact(period.Closes, "HH:mm", CultureInfo.InvariantCulture))));
}
