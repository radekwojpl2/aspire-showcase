using AspireShowcase.SharedKernel;

namespace AspireShowcase.BusinessSetup;

/// <summary>
/// A business on the platform, booked at /book/{Slug}: the aggregate root of Business Setup.
/// It's only changed through its methods, which keep its rules.
/// </summary>
sealed class Business
{
    public const int MinNameLength = 2;
    public const int MaxNameLength = 100;

    // For EF Core.
    Business()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = "";

    /// <summary>See <see cref="BookingSlug"/>. Unique across businesses, which the database enforces.</summary>
    public string Slug { get; private set; } = "";

    /// <summary>The Logto user ID (sub) of the owner. One business per owner for now.</summary>
    public string OwnerId { get; private set; } = "";

    /// <summary>The IANA time zone of <see cref="OpeningHours"/>; see <see cref="BusinessTimeZone"/>.</summary>
    public string TimeZone { get; private set; } = BusinessTimeZone.Default;

    /// <summary>Closed every day until the owner sets them.</summary>
    public OpeningHours OpeningHours { get; private set; } = OpeningHours.Closed;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Starts a business for its owner (user story MVP-8).</summary>
    /// <exception cref="DomainValidationException">The name, slug or time zone can't be used.</exception>
    public static Business Start(string? name, string? slug, string? timeZone, string ownerId, DateTimeOffset now)
    {
        var errors = new DomainErrors();
        var trimmedName = name?.Trim();
        if (trimmedName is not { Length: >= MinNameLength and <= MaxNameLength })
        {
            errors.Add("name", $"Use {MinNameLength} to {MaxNameLength} characters.");
        }
        if (BookingSlug.Problem(slug) is { } slugProblem)
        {
            errors.Add("slug", slugProblem);
        }
        CheckTimeZone(timeZone, errors);
        errors.ThrowIfAny();

        return new Business
        {
            Id = Guid.CreateVersion7(),
            Name = trimmedName!,
            Slug = slug!,
            OwnerId = ownerId,
            TimeZone = timeZone!,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Replaces the weekly opening hours and the time zone they're in (user story MVP-9).
    /// Existing bookings stay as they are: the hours only decide which new bookings are offered,
    /// so nothing here touches them.
    /// </summary>
    /// <exception cref="DomainValidationException">The time zone isn't a known IANA time zone.</exception>
    public void SetOpeningHours(OpeningHours hours, string? timeZone)
    {
        var errors = new DomainErrors();
        CheckTimeZone(timeZone, errors);
        errors.ThrowIfAny();

        OpeningHours = hours;
        TimeZone = timeZone!;
    }

    static void CheckTimeZone(string? timeZone, DomainErrors errors)
    {
        if (!BusinessTimeZone.IsValid(timeZone))
        {
            errors.Add("timeZone", "Choose a time zone from the list.");
        }
    }
}
