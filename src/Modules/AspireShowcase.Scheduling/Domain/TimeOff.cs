using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.SharedKernel;

namespace AspireShowcase.Scheduling;

/// <summary>The ID of a blocked time.</summary>
readonly record struct TimeOffId(Guid Value)
{
    public static TimeOffId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// A time nobody can book (user story V1-1): a holiday or a break, of the whole business or of
/// one staff member. An aggregate of Scheduling, which refers to the business and staff member by
/// ID.
/// </summary>
/// <remarks>
/// Blocking a time doesn't touch the bookings already in it: the owner gets them listed and
/// decides what to do. So time off isn't part of the database's no-overlap constraint, only of
/// the free slots.
/// </remarks>
sealed class TimeOff
{
    public const int MaxNoteLength = 200;

    /// <summary>The longest a single block can be; a longer absence is a few blocks.</summary>
    public static readonly TimeSpan MaxLength = TimeSpan.FromDays(366);

    // For EF Core.
    TimeOff()
    {
    }

    public TimeOffId Id { get; private set; }

    public BusinessId BusinessId { get; private set; }

    /// <summary>The staff member who's away, or null when the whole business is closed.</summary>
    public StaffMemberId? StaffMemberId { get; private set; }

    /// <summary>When it starts, as an instant (stored in UTC).</summary>
    public DateTimeOffset Start { get; private set; }

    /// <summary>When it ends: the first moment that can be booked again.</summary>
    public DateTimeOffset End { get; private set; }

    /// <summary>For the owner only, such as "Holiday"; clients never see it.</summary>
    public string? Note { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Blocks a time for the whole business, or for one of its staff members.</summary>
    /// <exception cref="DomainValidationException">The times or the note can't be used; every
    /// problem is reported at once.</exception>
    public static TimeOff Block(
        BusinessId businessId, StaffMemberId? staffMemberId, DateTimeOffset start, DateTimeOffset end, string? note,
        DateTimeOffset now)
    {
        var errors = new DomainErrors();
        if (end <= start)
        {
            errors.Add("to", "End it after it starts.");
        }
        else if (end - start > MaxLength)
        {
            errors.Add("to", "Block at most a year at a time.");
        }
        if (end <= now)
        {
            errors.Add("to", "This time is already over.");
        }
        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmedNote is { Length: > MaxNoteLength })
        {
            errors.Add("note", $"Use at most {MaxNoteLength} characters.");
        }
        errors.ThrowIfAny();

        return new TimeOff
        {
            Id = TimeOffId.New(),
            BusinessId = businessId,
            StaffMemberId = staffMemberId,
            Start = start.ToUniversalTime(),
            End = end.ToUniversalTime(),
            Note = trimmedNote,
            CreatedAt = now,
        };
    }

    /// <summary>Whether it keeps <paramref name="staffMemberId"/> from being booked.</summary>
    public bool Covers(StaffMemberId staffMemberId) => StaffMemberId is null || StaffMemberId == staffMemberId;

    /// <summary>Whether it overlaps [<paramref name="start"/>, <paramref name="end"/>).</summary>
    public bool Overlaps(DateTimeOffset start, DateTimeOffset end) => Start < end && start < End;
}
