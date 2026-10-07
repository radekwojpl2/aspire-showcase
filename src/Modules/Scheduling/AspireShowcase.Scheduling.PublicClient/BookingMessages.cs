namespace AspireShowcase.Scheduling.PublicClient;

// The messages Scheduling publishes over the message bus. IDs are plain GUIDs here: these cross
// process boundaries, so they're a wire format, not a domain model.

/// <summary>A client booked (user story MVP-4). Published with the booking, through the outbox.</summary>
public sealed record BookingConfirmed(Guid BookingId, Guid BusinessId, DateTimeOffset OccurredAt);

/// <summary>A booking was cancelled, by the client (MVP-7) or the business (MVP-14).</summary>
/// <param name="CancelledBy">client or business.</param>
public sealed record BookingCancelled(Guid BookingId, Guid BusinessId, string CancelledBy, DateTimeOffset OccurredAt);

/// <summary>A booking was moved to another time, by the client or the business (V1-4).</summary>
/// <param name="RescheduledBy">client or business.</param>
/// <param name="PreviousStart">When it was before.</param>
public sealed record BookingRescheduled(
    Guid BookingId, Guid BusinessId, string RescheduledBy, DateTimeOffset PreviousStart, DateTimeOffset OccurredAt);

/// <summary>
/// Everything a message to the client and the owner about a booking needs, in the business's
/// local time. Scheduling publishes it after a <see cref="BookingConfirmed"/> or
/// <see cref="BookingCancelled"/> or <see cref="BookingRescheduled"/>, once it has looked the details up; the notifications service
/// turns it into emails without asking anyone anything.
/// </summary>
/// <param name="Kind">confirmed, cancelled or rescheduled.</param>
/// <param name="CancelledBy">client or business, for a cancellation.</param>
/// <param name="Date">yyyy-MM-dd.</param>
/// <param name="Start">HH:mm.</param>
/// <param name="RescheduledBy">client or business, for a booking moved to another time (V1-4).</param>
/// <param name="PreviousDate">For a moved booking, its date before, yyyy-MM-dd.</param>
/// <param name="PreviousStart">For a moved booking, its start before, HH:mm.</param>
public sealed record BookingNotice(
    string Kind, string? CancelledBy, Guid BookingId, string BusinessName, string BusinessSlug, string ServiceName,
    string StaffName, string Date, string Start, string End, string TimeZone, BookingParty Client, BookingParty Owner,
    string? RescheduledBy = null, string? PreviousDate = null, string? PreviousStart = null);

/// <param name="Email">Null when their account has none: then they get no email.</param>
public sealed record BookingParty(string Name, string? Email);
