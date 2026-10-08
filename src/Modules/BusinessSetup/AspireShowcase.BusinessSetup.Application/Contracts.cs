namespace AspireShowcase.BusinessSetup.Application;

// What the use cases take and give back, as the API's JSON has them.

/// <param name="OwnerName">The owner's name as their staff member, as clients will see it.</param>
record StartBusiness(string? Name, string? Slug, string? TimeZone, string? ContactEmail, string? OwnerName);

/// <param name="ContactEmail">Null for a business started before it was required, until the owner adds one.</param>
record BusinessResponse(Guid Id, string Name, string Slug, string TimeZone, string? ContactEmail, DateTimeOffset CreatedAt);

record ContactBody(string? ContactEmail);

/// <param name="Problem">Why the link can't be used, when it can't.</param>
record SlugAvailability(string Slug, bool Available, string? Problem);

/// <summary>Opening hours as the API sends and takes them: weekdays by name, times as HH:mm.</summary>
record OpeningHoursBody(string? TimeZone, List<OpeningPeriodBody>? Periods);

record OpeningPeriodBody(string? Day, string? Opens, string? Closes);

/// <summary>
/// A service as the API takes it: the duration and buffer in minutes (no buffer when left out),
/// the price as an amount and an ISO currency.
/// </summary>
record ServiceBody(string? Name, int? DurationMinutes, int? BufferMinutes, decimal? Price, string? Currency);

record ServiceResponse(
    Guid Id, string Name, int DurationMinutes, int BufferMinutes, decimal Price, string Currency, bool IsHidden);

/// <param name="ServiceIds">The services they do, unless <paramref name="DoesAllServices"/>.</param>
/// <param name="WorkingHours">Their own hours, or null to work whenever the business is open.</param>
record StaffBody(string? Name, bool? DoesAllServices, List<Guid>? ServiceIds, List<OpeningPeriodBody>? WorkingHours);

/// <param name="IsOwner">The owner's own staff member, created with the business.</param>
record StaffResponse(
    Guid Id, string Name, bool IsOwner, bool DoesAllServices, IReadOnlyList<Guid> ServiceIds,
    List<OpeningPeriodBody>? WorkingHours);
