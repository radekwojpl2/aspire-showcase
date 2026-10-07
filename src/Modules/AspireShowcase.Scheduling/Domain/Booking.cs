using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.SharedKernel;

namespace AspireShowcase.Scheduling;

/// <summary>The ID of a booking.</summary>
readonly record struct BookingId(Guid Value)
{
    public static BookingId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

enum BookingStatus
{
    Confirmed,
    Cancelled,
}

/// <summary>Who moved a booking to another time (V1-4): the client, or the business for them.</summary>
enum RescheduledBy
{
    Client,
    Business,
}

/// <summary>Who cancelled a booking: the client (MVP-7), or the business for them (MVP-14).</summary>
enum CancelledBy
{
    Client,
    Business,
}

/// <summary>
/// A client's appointment with one staff member for one service: the aggregate root of
/// Scheduling. It refers to the business, staff member and service by ID; they belong to
/// Business Setup.
/// </summary>
/// <remarks>
/// A small aggregate on purpose. No two confirmed bookings of the same staff member may overlap,
/// and the database enforces that with an exclusion constraint (see BookingConfiguration), which
/// a save turns into <see cref="BookingResult.SlotTaken"/>. A per-staff "calendar" aggregate
/// could keep the rule in code instead, but every booking of a staff member would then contend
/// for the same object. Bookings of different staff members can overlap freely.
/// </remarks>
sealed class Booking
{
    readonly List<BookingEvent> _events = [];

    // For EF Core.
    Booking()
    {
    }

    public BookingId Id { get; private set; }

    public BusinessId BusinessId { get; private set; }

    /// <summary>Who it's with; moving it (V1-4) can change that.</summary>
    public StaffMemberId StaffMemberId { get; private set; }

    public ServiceId ServiceId { get; private set; }

    /// <summary>When it starts, as an instant (stored in UTC).</summary>
    public DateTimeOffset Start { get; private set; }

    /// <summary>When it ends: the start plus the service's duration at the time of booking.</summary>
    public DateTimeOffset End { get; private set; }

    /// <summary>
    /// Until when the staff member can't be booked again: the end plus the service's buffer at the
    /// time of booking (user story V1-2). Clients see only the start and end; the no-overlap
    /// constraint uses this.
    /// </summary>
    public DateTimeOffset OccupiedUntil { get; private set; }

    /// <summary>
    /// How long before the start the client can still cancel or move it themselves: the
    /// business's cancellation policy when it was booked (user story V1-3). Zero for none.
    /// </summary>
    public TimeSpan ChangeNotice { get; private set; }

    /// <summary>Until when the client can still cancel or move it themselves.</summary>
    public DateTimeOffset ClientCanChangeUntil => Start - ChangeNotice;

    public Attendee Attendee { get; private set; } = null!; // Set by Book, or by EF Core when loaded.

    public BookingStatus Status { get; private set; }

    /// <summary>When it was cancelled, if it was.</summary>
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>
    /// Who cancelled it, if it was: the message to the client differs ("you cancelled", or "the
    /// business had to cancel").
    /// </summary>
    public CancelledBy? CancelledBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>What happened since it was loaded, until it's saved: see <see cref="BookingEvent"/>.</summary>
    public IReadOnlyList<BookingEvent> Events => _events;

    /// <summary>
    /// Moves the booking to another time, with the same or another staff member (user story V1-4).
    /// It keeps its length and buffer, and the row is updated in place: the old time is only free
    /// once the new one is saved, and moving it a little later doesn't collide with itself. Whether
    /// the new time is free is the database's call, as when booking. Moving it where it is
    /// already changes nothing.
    /// </summary>
    /// <exception cref="DomainValidationException">It's cancelled or has started, the new time
    /// has started, or a client is too late under the cancellation policy (V1-3).</exception>
    public void Reschedule(DateTimeOffset start, StaffMemberId staffMemberId, DateTimeOffset now, RescheduledBy by)
    {
        var errors = new DomainErrors();
        if (Status == BookingStatus.Cancelled)
        {
            errors.Add("booking", "It's cancelled, so it can't be moved.");
        }
        else if (Start <= now)
        {
            errors.Add("booking", "It has already started, so it can't be moved.");
        }
        if (start <= now)
        {
            errors.Add("startsAt", "Choose a time that hasn't started.");
        }
        errors.ThrowIfAny();
        if (by == RescheduledBy.Client)
        {
            ThrowIfTooLateForClient(now, "move it");
        }
        if (start == Start && staffMemberId == StaffMemberId)
        {
            return;
        }

        var (previousStart, previousStaffMemberId) = (Start, StaffMemberId);
        var length = End - Start;
        var buffer = OccupiedUntil - End;
        Start = start.ToUniversalTime();
        End = Start + length;
        OccupiedUntil = End + buffer;
        StaffMemberId = staffMemberId;
        _events.Add(new BookingRescheduled(Id, now, previousStart, previousStaffMemberId, by));
    }

    /// <exception cref="DomainValidationException">The client's notice period has begun.</exception>
    void ThrowIfTooLateForClient(DateTimeOffset now, string change)
    {
        if (now > ClientCanChangeUntil)
        {
            var errors = new DomainErrors();
            errors.Add("booking", $"It's too late to {change} online: contact the business.");
            errors.ThrowIfAny();
        }
    }

    /// <summary>Once the events are in the outbox, or when nobody should hear about them.</summary>
    public void ClearEvents() => _events.Clear();

    /// <summary>Books a staff member for a service. Whether the time is free is the database's call.</summary>
    /// <param name="buffer">The service's time kept free after it; zero for none.</param>
    /// <param name="changeNotice">The business's cancellation notice, kept with the booking.</param>
    /// <exception cref="DomainValidationException">The duration isn't positive, or the buffer is negative.</exception>
    public static Booking Book(
        BusinessId businessId, StaffMemberId staffMemberId, ServiceId serviceId, DateTimeOffset start, TimeSpan duration,
        TimeSpan buffer, TimeSpan changeNotice, Attendee attendee, DateTimeOffset now)
    {
        var errors = new DomainErrors();
        if (duration <= TimeSpan.Zero)
        {
            errors.Add("serviceId", "The service has no duration.");
        }
        if (buffer < TimeSpan.Zero)
        {
            errors.Add("serviceId", "The service's buffer is negative.");
        }
        errors.ThrowIfAny();

        var booking = new Booking
        {
            Id = BookingId.New(),
            BusinessId = businessId,
            StaffMemberId = staffMemberId,
            ServiceId = serviceId,
            Start = start.ToUniversalTime(),
            End = (start + duration).ToUniversalTime(),
            OccupiedUntil = (start + duration + buffer).ToUniversalTime(),
            ChangeNotice = changeNotice,
            Attendee = attendee,
            Status = BookingStatus.Confirmed,
            CreatedAt = now,
        };
        booking._events.Add(new BookingConfirmed(booking.Id, now));
        return booking;
    }

    /// <summary>
    /// Cancels the booking: by the client (MVP-7), or by the business, for sickness or emergencies
    /// (MVP-14). Its time is free again at once: the no-overlap constraint only counts confirmed
    /// bookings. Cancelling twice changes nothing.
    /// </summary>
    /// <exception cref="DomainValidationException">It has already started, or a client is too late
    /// under the cancellation policy (V1-3); the business can still cancel it.</exception>
    public void Cancel(DateTimeOffset now, CancelledBy by)
    {
        if (Status == BookingStatus.Cancelled)
        {
            return;
        }
        if (Start <= now)
        {
            var errors = new DomainErrors();
            errors.Add("booking", "It has already started, so it can't be cancelled.");
            errors.ThrowIfAny();
        }
        if (by == Scheduling.CancelledBy.Client)
        {
            ThrowIfTooLateForClient(now, "cancel");
        }

        Status = BookingStatus.Cancelled;
        CancelledAt = now;
        CancelledBy = by;
        _events.Add(new BookingCancelled(Id, now, by));
    }
}

/// <summary>
/// Something that happened to a booking, which others react to (the Notifications module sends
/// emails). The aggregate records it; it's saved to the outbox in the same transaction.
/// </summary>
abstract record BookingEvent(BookingId BookingId, DateTimeOffset OccurredAt);

/// <summary>A client booked (user story MVP-4).</summary>
sealed record BookingConfirmed(BookingId BookingId, DateTimeOffset OccurredAt) : BookingEvent(BookingId, OccurredAt);

/// <summary>The client (MVP-7) or the business (MVP-14) cancelled.</summary>
sealed record BookingCancelled(BookingId BookingId, DateTimeOffset OccurredAt, CancelledBy By)
    : BookingEvent(BookingId, OccurredAt);

/// <summary>The client or the business moved it to another time (V1-4).</summary>
sealed record BookingRescheduled(
    BookingId BookingId, DateTimeOffset OccurredAt, DateTimeOffset PreviousStart, StaffMemberId PreviousStaffMemberId,
    RescheduledBy By) : BookingEvent(BookingId, OccurredAt);

/// <summary>What became of an attempt to book.</summary>
enum BookingResult
{
    Booked,

    /// <summary>Another confirmed booking of the same staff member overlaps it.</summary>
    SlotTaken,
}
