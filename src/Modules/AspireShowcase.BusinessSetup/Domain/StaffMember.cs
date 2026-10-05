using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.SharedKernel;

namespace AspireShowcase.BusinessSetup;

/// <summary>
/// Someone clients can be booked with: an aggregate of Business Setup that refers to its
/// <see cref="Business"/> by ID. Each staff member has their own bookings, so different staff
/// can be booked at the same time (user story MVP-11).
/// </summary>
/// <remarks>
/// By default a staff member does every service of the business, including ones added later, and
/// works whenever the business is open; that's why a one-person business needs no setup. Both can
/// be narrowed. Working hours have to lie within the opening hours, a rule that crosses this
/// aggregate and <see cref="Business"/>, which is why they share a module. Names are unique per
/// business, which the database enforces.
/// </remarks>
sealed class StaffMember
{
    public const int MinNameLength = 1;
    public const int MaxNameLength = 80;

    /// <summary>The owner's name, until they change it, when sign-in didn't give one.</summary>
    public const string DefaultOwnerName = "Owner";

    readonly List<ServiceId> _serviceIds = [];

    // For EF Core.
    StaffMember()
    {
    }

    public StaffMemberId Id { get; private set; }

    public BusinessId BusinessId { get; private set; }

    /// <summary>As clients see it when they choose who to book with.</summary>
    public string Name { get; private set; } = "";

    /// <summary>
    /// The Logto user ID (sub) of the person, when they have an account: so far only the owner's
    /// own staff member. Staff sign in themselves from v2.
    /// </summary>
    public string? UserId { get; private set; }

    /// <summary>Does every service of the business, including ones added later.</summary>
    public bool DoesAllServices { get; private set; } = true;

    /// <summary>The services they do, when not <see cref="DoesAllServices"/>.</summary>
    public IReadOnlyList<ServiceId> ServiceIds => _serviceIds;

    /// <summary>Their own hours, or null to work whenever the business is open.</summary>
    public WeeklyHours? WorkingHours { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// The owner, as the business's first staff member: created with the business, so a
    /// one-person business can be booked without setting anything up.
    /// </summary>
    /// <param name="ownerId">The owner's Logto user ID, the business's <see cref="Business.OwnerId"/>.</param>
    public static StaffMember ForOwner(BusinessId businessId, string ownerId, string? name, DateTimeOffset now) => new()
    {
        Id = StaffMemberId.New(),
        BusinessId = businessId,
        Name = name?.Trim() is { Length: >= MinNameLength and <= MaxNameLength } trimmed ? trimmed : DefaultOwnerName,
        UserId = ownerId,
        CreatedAt = now,
    };

    /// <summary>Adds a staff member to a business (user story MVP-11).</summary>
    /// <param name="serviceIds">The services they do, unless <paramref name="doesAllServices"/>.</param>
    /// <param name="workingHours">Their own hours, or null to work whenever the business is open.</param>
    /// <param name="businessServiceIds">Every service of the business, to check the chosen ones against.</param>
    /// <param name="openingHours">The business's opening hours, which working hours have to stay within.</param>
    /// <exception cref="DomainValidationException">The name, services or hours can't be used; every
    /// problem is reported at once.</exception>
    public static StaffMember Add(
        BusinessId businessId, string? name, bool doesAllServices, IEnumerable<ServiceId>? serviceIds,
        IReadOnlyCollection<ServiceId> businessServiceIds, WeeklyHours? workingHours, WeeklyHours openingHours,
        DateTimeOffset now)
    {
        var staffMember = new StaffMember { Id = StaffMemberId.New(), BusinessId = businessId, CreatedAt = now };
        staffMember.Change(name, doesAllServices, serviceIds, businessServiceIds, workingHours, openingHours);
        return staffMember;
    }

    /// <summary>Changes the name, the services and the working hours together, as the owner edits them.</summary>
    /// <param name="openingHours">The business's opening hours, which working hours have to stay within.</param>
    /// <exception cref="DomainValidationException">The name, services or hours can't be used; every
    /// problem is reported at once.</exception>
    public void Change(
        string? name, bool doesAllServices, IEnumerable<ServiceId>? serviceIds,
        IReadOnlyCollection<ServiceId> businessServiceIds, WeeklyHours? workingHours, WeeklyHours openingHours)
    {
        var errors = new DomainErrors();
        var trimmedName = name?.Trim();
        if (trimmedName is not { Length: >= MinNameLength and <= MaxNameLength })
        {
            errors.Add("name", $"Use {MinNameLength} to {MaxNameLength} characters.");
        }

        var chosen = doesAllServices ? [] : (serviceIds ?? []).Distinct().ToList();
        if (!doesAllServices && chosen.Count == 0)
        {
            errors.Add("serviceIds", "Choose at least one service, or all of them.");
        }
        else if (chosen.Any(id => !businessServiceIds.Contains(id)))
        {
            errors.Add("serviceIds", "Choose services of this business.");
        }

        // The rule that crosses aggregates: staff only work while the business is open.
        foreach (var period in workingHours?.OutsideOf(openingHours) ?? [])
        {
            errors.Add(WeeklyHours.FieldName(period.Day), $"{WeeklyHours.Describe(period)} is outside the opening hours.");
        }

        errors.ThrowIfAny();

        Name = trimmedName!;
        DoesAllServices = doesAllServices;
        _serviceIds.Clear();
        _serviceIds.AddRange(chosen);
        WorkingHours = workingHours;
    }

    /// <summary>Whether clients can book them for the service.</summary>
    public bool Does(ServiceId serviceId) => DoesAllServices || _serviceIds.Contains(serviceId);
}

/// <summary>
/// A staff member's own working hours, by name, for <see cref="Business.SetOpeningHours"/> to
/// check against: the values it needs, not the StaffMember aggregate.
/// </summary>
sealed record StaffHours(string StaffName, WeeklyHours WorkingHours);
