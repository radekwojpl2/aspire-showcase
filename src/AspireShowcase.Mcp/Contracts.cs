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
