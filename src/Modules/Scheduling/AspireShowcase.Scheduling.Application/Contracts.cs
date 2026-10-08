namespace AspireShowcase.Scheduling.Application;

// What the use cases take and give back, as the API's JSON has them.

// The public booking page (MVP-1 to MVP-4).

record PublicStaff(Guid Id, string Name);

record PublicService(
    Guid Id, string Name, int DurationMinutes, decimal Price, string Currency, IReadOnlyList<PublicStaff> Staff);

/// <param name="CancellationNoticeHours">Clients can cancel or move a booking up to this many hours before it; 0 for until it starts.</param>
/// <param name="Address">Where it is (V1-6), one line per line of the address; like <paramref name="Description"/>
/// and <paramref name="LogoUrl"/>, null for none.</param>
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

// A client's own bookings (MVP-7, V1-3, V1-4).

/// <param name="Date">The local date in the business's time zone, yyyy-MM-dd.</param>
/// <param name="Start">The local time, HH:mm.</param>
/// <param name="CanChangeUntil">Until when the client can cancel or move it themselves (V1-3).</param>
/// <param name="BusinessContactEmail">Where to reach the business after that; null if it has none yet.</param>
record ClientBooking(
    Guid Id, string BusinessName, string BusinessSlug, string ServiceName, string StaffName, string Date, string Start,
    string End, string TimeZone, DateTimeOffset CanChangeUntil, string? BusinessContactEmail);

/// <param name="StaffMemberId">Who it should be with, or null for anyone who's free (whoever it's with now first).</param>
record RescheduleBody(DateTimeOffset? StartsAt, Guid? StaffMemberId);

// The owner's calendar (MVP-12, V1-1).

/// <param name="Day">The local date, yyyy-MM-dd.</param>
/// <param name="Start">The local time, HH:mm, in the business's time zone.</param>
record CalendarBooking(
    Guid Id, string Day, string Start, string End, Guid StaffMemberId, string StaffName, string ServiceName,
    string ClientName, string ClientEmail);

record CalendarStaff(Guid Id, string Name);

/// <summary>The part of a time off (V1-1) that falls on one day of the calendar.</summary>
/// <param name="End">HH:mm, or 24:00 when it lasts to the end of the day.</param>
/// <param name="StaffMemberId">Null for the whole business.</param>
record CalendarTimeOff(
    Guid Id, string Day, string Start, string End, Guid? StaffMemberId, string? StaffName, string? Note);

/// <param name="Date">The date asked for; <paramref name="FirstDay"/> to <paramref name="LastDay"/> is what's shown.</param>
record CalendarResponse(
    string View, string Date, string FirstDay, string LastDay, string TimeZone, IReadOnlyList<CalendarStaff> Staff,
    IReadOnlyList<CalendarBooking> Bookings, IReadOnlyList<CalendarTimeOff> TimeOff);

// Time off (V1-1).

/// <param name="StaffMemberId">Who's away, or null for the whole business.</param>
/// <param name="From">The local start, yyyy-MM-ddTHH:mm, in the business's time zone.</param>
/// <param name="To">The local end, the same way; midnight for a whole last day.</param>
record TimeOffBody(Guid? StaffMemberId, string? From, string? To, string? Note);

/// <param name="Bookings">The confirmed bookings inside it, which the owner may want to cancel.</param>
record TimeOffResponse(
    Guid Id, Guid? StaffMemberId, string? StaffName, string From, string To, string? Note,
    IReadOnlyList<TimeOffBooking> Bookings);

/// <param name="Day">The local date, yyyy-MM-dd.</param>
/// <param name="Start">The local time, HH:mm.</param>
record TimeOffBooking(
    Guid Id, string Day, string Start, string End, string StaffName, string ServiceName, string ClientName);

record TimeOffList(string TimeZone, IReadOnlyList<TimeOffResponse> TimeOff);

// The cancellation policy (V1-3).

/// <param name="NoticeHours">How many hours before a booking clients can still cancel or move it; 0 for until it starts.</param>
record CancellationPolicyBody(int? NoticeHours);

// Development tools.

record SampleBookingsResult(int Booked, int SlotTaken);
