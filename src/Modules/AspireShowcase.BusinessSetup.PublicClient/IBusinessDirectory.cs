namespace AspireShowcase.BusinessSetup.PublicClient;

/// <summary>
/// What other modules may read from Business Setup: businesses, their staff and services, as
/// plain data. Business Setup's aggregates stay internal; this is the only way in.
/// </summary>
/// <remarks>
/// A synchronous, in-process read for now. ddd-modules.md has Scheduling keep its own copies of
/// these, built from Business Setup's events, so the slot calculation never has to ask; that
/// comes when there are events (with the public booking page, MVP-1).
/// </remarks>
public interface IBusinessDirectory
{
    /// <summary>The business the user owns, or null when they have none.</summary>
    Task<BusinessInfo?> FindOwnedAsync(string ownerId, CancellationToken cancellation);

    /// <summary>The business booked at /book/{slug}, or null when there's none.</summary>
    Task<BusinessInfo?> FindBySlugAsync(string slug, CancellationToken cancellation);

    /// <summary>Every business. For development tools, such as sample data.</summary>
    Task<IReadOnlyList<BusinessInfo>> ListAsync(CancellationToken cancellation);

    /// <summary>The business's staff, with the hours they actually work.</summary>
    Task<IReadOnlyList<StaffInfo>> StaffAsync(BusinessId businessId, CancellationToken cancellation);

    /// <summary>The business's services, hidden ones included.</summary>
    Task<IReadOnlyList<ServiceInfo>> ServicesAsync(BusinessId businessId, CancellationToken cancellation);
}

/// <param name="TimeZone">The IANA time zone its hours are in.</param>
public sealed record BusinessInfo(BusinessId Id, string Name, string Slug, string TimeZone);

/// <param name="ServiceIds">The services they do; every service when <paramref name="DoesAllServices"/>.</param>
/// <param name="WorkingHours">When they work: their own hours, or else the business's opening hours.</param>
public sealed record StaffInfo(
    StaffMemberId Id, string Name, bool DoesAllServices, IReadOnlyList<ServiceId> ServiceIds, IReadOnlyList<WorkingPeriod> WorkingHours);

/// <summary>A stretch of a weekday, in the business's local time: [Start, End).</summary>
public sealed record WorkingPeriod(DayOfWeek Day, TimeOnly Start, TimeOnly End);

/// <param name="Price">Shown to clients, in <paramref name="Currency"/> (ISO 4217); nothing is paid online.</param>
public sealed record ServiceInfo(
    ServiceId Id, string Name, TimeSpan Duration, decimal Price, string Currency, bool IsHidden);
