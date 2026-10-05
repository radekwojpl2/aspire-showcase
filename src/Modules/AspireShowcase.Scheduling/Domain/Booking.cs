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
    // For EF Core.
    Booking()
    {
    }

    public BookingId Id { get; private set; }

    public BusinessId BusinessId { get; private set; }

    public StaffMemberId StaffMemberId { get; private set; }

    public ServiceId ServiceId { get; private set; }

    /// <summary>When it starts, as an instant (stored in UTC).</summary>
    public DateTimeOffset Start { get; private set; }

    /// <summary>When it ends: the start plus the service's duration at the time of booking.</summary>
    public DateTimeOffset End { get; private set; }

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

    /// <summary>Books a staff member for a service. Whether the time is free is the database's call.</summary>
    /// <exception cref="DomainValidationException">The duration isn't positive.</exception>
    public static Booking Book(
        BusinessId businessId, StaffMemberId staffMemberId, ServiceId serviceId, DateTimeOffset start, TimeSpan duration,
        Attendee attendee, DateTimeOffset now)
    {
        if (duration <= TimeSpan.Zero)
        {
            var errors = new DomainErrors();
            errors.Add("serviceId", "The service has no duration.");
            errors.ThrowIfAny();
        }

        return new Booking
        {
            Id = BookingId.New(),
            BusinessId = businessId,
            StaffMemberId = staffMemberId,
            ServiceId = serviceId,
            Start = start.ToUniversalTime(),
            End = (start + duration).ToUniversalTime(),
            Attendee = attendee,
            Status = BookingStatus.Confirmed,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Cancels the booking: by the client (MVP-7), or by the business, for sickness or emergencies
    /// (MVP-14). Its time is free again at once: the no-overlap constraint only counts confirmed
    /// bookings. Cancelling twice changes nothing.
    /// </summary>
    /// <exception cref="DomainValidationException">It has already started.</exception>
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

        Status = BookingStatus.Cancelled;
        CancelledAt = now;
        CancelledBy = by;
    }
}

/// <summary>What became of an attempt to book.</summary>
enum BookingResult
{
    Booked,

    /// <summary>Another confirmed booking of the same staff member overlaps it.</summary>
    SlotTaken,
}
