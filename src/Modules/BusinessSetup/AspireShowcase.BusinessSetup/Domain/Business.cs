using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.BuildingBlocks.Domain;

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

    public BusinessId Id { get; private set; }

    public string Name { get; private set; } = "";

    /// <summary>See <see cref="BookingSlug"/>. Unique across businesses, which the database enforces.</summary>
    public string Slug { get; private set; } = "";

    /// <summary>The Logto user ID (sub) of the owner. One business per owner for now.</summary>
    public string OwnerId { get; private set; } = "";

    /// <summary>The IANA time zone of <see cref="OpeningHours"/>; see <see cref="BusinessTimeZone"/>.</summary>
    public string TimeZone { get; private set; } = BusinessTimeZone.Default;

    /// <summary>
    /// Where clients can reach the business; see <see cref="BusinessSetup.ContactEmail"/>. Required
    /// for a new business, so null only for one started before it was.
    /// </summary>
    public string? ContactEmail { get; private set; }

    /// <summary>Closed every day until the owner sets them.</summary>
    public WeeklyHours OpeningHours { get; private set; } = WeeklyHours.Closed;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Starts a business for its owner (user story MVP-8).</summary>
    /// <exception cref="DomainValidationException">The name, slug, time zone or contact email can't be used.</exception>
    public static Business Start(
        string? name, string? slug, string? timeZone, string? contactEmail, string ownerId, DateTimeOffset now)
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
        CheckContactEmail(contactEmail, errors);
        errors.ThrowIfAny();

        return new Business
        {
            Id = BusinessId.New(),
            Name = trimmedName!,
            Slug = slug!,
            OwnerId = ownerId,
            TimeZone = timeZone!,
            ContactEmail = contactEmail!.Trim(),
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Replaces the weekly opening hours and the time zone they're in (user story MVP-9).
    /// Existing bookings stay as they are: the hours only decide which new bookings are offered,
    /// so nothing here touches them.
    /// </summary>
    /// <param name="staffHours">The working hours of the staff who have their own, which have to
    /// stay within the new opening hours (user story MVP-11). Staff who work whenever the business
    /// is open follow the change on their own. Only the hours, not the staff members: an aggregate
    /// is never handed another one.</param>
    /// <exception cref="DomainValidationException">The time zone isn't a known IANA time zone, or
    /// the new hours would leave someone's working hours outside them (under "staff").</exception>
    public void SetOpeningHours(WeeklyHours hours, string? timeZone, IEnumerable<StaffHours> staffHours)
    {
        var errors = new DomainErrors();
        CheckTimeZone(timeZone, errors);
        foreach (var (staffName, workingHours) in staffHours)
        {
            var outside = workingHours.OutsideOf(hours);
            if (outside.Count > 0)
            {
                errors.Add("staff",
                    $"{staffName} works {string.Join(", ", outside.Select(WeeklyHours.Describe))}, outside these hours. " +
                    "Change their working hours first.");
            }
        }
        errors.ThrowIfAny();

        OpeningHours = hours;
        TimeZone = timeZone!;
    }

    /// <summary>Changes where clients can reach the business.</summary>
    /// <exception cref="DomainValidationException">It isn't an email address.</exception>
    public void ChangeContactEmail(string? contactEmail)
    {
        var errors = new DomainErrors();
        CheckContactEmail(contactEmail, errors);
        errors.ThrowIfAny();

        ContactEmail = contactEmail!.Trim();
    }

    static void CheckContactEmail(string? contactEmail, DomainErrors errors)
    {
        if (BusinessSetup.ContactEmail.Problem(contactEmail) is { } problem)
        {
            errors.Add("contactEmail", problem);
        }
    }

    static void CheckTimeZone(string? timeZone, DomainErrors errors)
    {
        if (!BusinessTimeZone.IsValid(timeZone))
        {
            errors.Add("timeZone", "Choose a time zone from the list.");
        }
    }
}
