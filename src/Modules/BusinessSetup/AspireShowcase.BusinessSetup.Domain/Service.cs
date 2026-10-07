using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.BuildingBlocks.Domain;

namespace AspireShowcase.BusinessSetup.Domain;

/// <summary>
/// Something a business offers, such as "Haircut, 45 min, 120 PLN": an aggregate of Business
/// Setup of its own, which refers to its <see cref="Business"/> by ID. Clients book a service, so
/// its duration is how much time a booking takes.
/// </summary>
/// <remarks>
/// A separate aggregate rather than a list inside Business: services change independently, and
/// Scheduling will need them one by one. Their names are unique per business, which the database
/// enforces, as it does for booking links.
/// </remarks>
sealed class Service
{
    public const int MinNameLength = 2;
    public const int MaxNameLength = 80;

    /// <summary>Durations come in 5-minute steps, like opening hours.</summary>
    public const int DurationStepMinutes = WeeklyHours.StepMinutes;

    public const int MaxDurationMinutes = 8 * 60;

    public const int MaxBufferMinutes = 2 * 60;

    // For EF Core.
    Service()
    {
    }

    public ServiceId Id { get; private set; }

    public BusinessId BusinessId { get; private set; }

    public string Name { get; private set; } = "";

    public TimeSpan Duration { get; private set; }

    /// <summary>
    /// Time kept free after each booking, such as 10 minutes to clean up (user story V1-2). Clients
    /// see only the duration; nobody can be booked during the buffer.
    /// </summary>
    public TimeSpan Buffer { get; private set; }

    /// <summary>Shown to clients; nothing is paid online.</summary>
    public Money Price { get; private set; } = null!; // Set by Add, or by EF Core when loaded.

    /// <summary>
    /// A hidden service stays with its business but isn't offered to clients any more. Services
    /// are hidden rather than deleted, so bookings made for them keep pointing at something.
    /// </summary>
    public bool IsHidden { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Adds a service to a business (user story MVP-10). It's offered to clients straight away.</summary>
    /// <param name="price">With <paramref name="currency"/>, the price shown to clients.</param>
    /// <param name="bufferMinutes">None when null.</param>
    /// <exception cref="DomainValidationException">The name, duration, buffer or price can't be
    /// used; every problem is reported at once.</exception>
    public static Service Add(
        BusinessId businessId, string? name, int? durationMinutes, int? bufferMinutes, decimal? price, string? currency,
        DateTimeOffset now)
    {
        var service = new Service { Id = ServiceId.New(), BusinessId = businessId, CreatedAt = now };
        service.Change(name, durationMinutes, bufferMinutes, price, currency);
        return service;
    }

    /// <summary>
    /// Changes the name, duration, buffer and price. Bookings already made keep the time they were
    /// booked for, buffer included; the new duration and buffer apply to new bookings.
    /// </summary>
    /// <param name="bufferMinutes">None when null.</param>
    /// <exception cref="DomainValidationException">The name, duration, buffer or price can't be
    /// used; every problem is reported at once.</exception>
    public void Change(string? name, int? durationMinutes, int? bufferMinutes, decimal? price, string? currency)
    {
        var errors = new DomainErrors();
        var trimmedName = name?.Trim();
        if (trimmedName is not { Length: >= MinNameLength and <= MaxNameLength })
        {
            errors.Add("name", $"Use {MinNameLength} to {MaxNameLength} characters.");
        }
        if (durationMinutes is not (>= DurationStepMinutes and <= MaxDurationMinutes) || durationMinutes % DurationStepMinutes != 0)
        {
            errors.Add("durationMinutes",
                $"Use {DurationStepMinutes}-minute steps, from {DurationStepMinutes} minutes to {MaxDurationMinutes / 60} hours.");
        }
        var buffer = bufferMinutes ?? 0;
        if (buffer is < 0 or > MaxBufferMinutes || buffer % DurationStepMinutes != 0)
        {
            errors.Add("bufferMinutes",
                $"Use {DurationStepMinutes}-minute steps, from none to {MaxBufferMinutes / 60} hours.");
        }
        var money = Money.Create(price, currency, errors);
        errors.ThrowIfAny();

        Name = trimmedName!;
        Duration = TimeSpan.FromMinutes(durationMinutes!.Value);
        Buffer = TimeSpan.FromMinutes(buffer);
        Price = money!;
    }

    /// <summary>Stops offering the service to clients, without deleting it.</summary>
    public void Hide() => IsHidden = true;

    /// <summary>Offers a hidden service to clients again.</summary>
    public void Show() => IsHidden = false;
}
