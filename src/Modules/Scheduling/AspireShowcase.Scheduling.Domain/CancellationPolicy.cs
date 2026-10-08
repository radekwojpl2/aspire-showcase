using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.BuildingBlocks.Domain;

namespace AspireShowcase.Scheduling.Domain;

/// <summary>
/// How late clients may still cancel or move a booking themselves (user stories V1-3 and V1-4):
/// up to <see cref="Notice"/> before it starts. One per business; a business without one has no
/// notice, so clients can change a booking until it starts.
/// </summary>
/// <remarks>
/// It belongs to Scheduling, which enforces it, rather than Business Setup, where the owner
/// edits it. A booking keeps the notice it was made under (<see cref="Booking.ChangeNotice"/>),
/// so tightening the policy doesn't catch out clients who booked under the old one.
/// </remarks>
sealed class CancellationPolicy
{
    /// <summary>A week: any longer and clients could hardly change anything.</summary>
    public const int MaxNoticeHours = 7 * 24;

    // For EF Core.
    CancellationPolicy()
    {
    }

    public BusinessId BusinessId { get; private set; }

    public TimeSpan Notice { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    /// <summary>The policy of a business that hasn't set one.</summary>
    public static CancellationPolicy None(BusinessId businessId) => new() { BusinessId = businessId };

    /// <exception cref="DomainValidationException">The hours are missing or out of range.</exception>
    public void Change(int? noticeHours, DateTimeOffset now)
    {
        if (noticeHours is not (>= 0 and <= MaxNoticeHours))
        {
            var errors = new DomainErrors();
            errors.Add("noticeHours", $"Use whole hours from 0 (until it starts) to {MaxNoticeHours} (a week).");
            errors.ThrowIfAny();
        }

        Notice = TimeSpan.FromHours(noticeHours!.Value);
        ChangedAt = now;
    }
}
