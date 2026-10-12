namespace AspireShowcase.Mcp;

// The API's JSON, as the tools read it and give it to the model; the same shapes as
// AspireShowcase.Scheduling.Application/Contracts.cs, which the MCP server doesn't reference.

record PublicStaff(Guid Id, string Name);

record PublicService(
    Guid Id, string Name, int DurationMinutes, decimal Price, string Currency, IReadOnlyList<PublicStaff> Staff);

record PublicBusiness(
    string Name, string Slug, string TimeZone, int CancellationNoticeHours, IReadOnlyList<PublicService> Services,
    string? Address, string? Description, string? LogoUrl);

/// <param name="Start">The local time, HH:mm.</param>
/// <param name="StartsAt">The instant, to book it with.</param>
record PublicSlot(string Start, DateTimeOffset StartsAt);

/// <param name="Date">The local date, yyyy-MM-dd.</param>
record PublicDay(string Date, IReadOnlyList<PublicSlot> Slots);

record PublicSlots(string TimeZone, IReadOnlyList<PublicDay> Days);

/// <param name="StaffMemberId">Who to book, or null for anyone who's free.</param>
record BookSlot(Guid? ServiceId, Guid? StaffMemberId, DateTimeOffset? StartsAt, string? ClientName, string? ClientEmail);

record BookingConfirmation(Guid Id, string ServiceName, string StaffName, string Date, string Start, string End);

/// <param name="Date">The local date in the business's time zone, yyyy-MM-dd.</param>
/// <param name="Start">The local time, HH:mm.</param>
/// <param name="CanChangeUntil">Until when the client can cancel or move it themselves.</param>
/// <param name="BusinessContactEmail">Where to reach the business after that; null if it has none yet.</param>
record ClientBooking(
    Guid Id, string BusinessName, string BusinessSlug, string ServiceName, string StaffName, string Date, string Start,
    string End, string TimeZone, DateTimeOffset CanChangeUntil, string? BusinessContactEmail);
