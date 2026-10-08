using System.Net.Mail;
using AspireShowcase.BuildingBlocks.Domain;

namespace AspireShowcase.Scheduling.Domain;

/// <summary>
/// Who a booking is for. A value object rather than a link to a Clients module: it also covers
/// bookings an owner makes for someone without an account (V1-5), and there's no Clients module
/// before v2.
/// </summary>
/// <param name="UserId">The Logto user ID (sub) when the client booked signed in.</param>
/// <param name="Email">Null only once the client deleted their account (V1-8).</param>
sealed record Attendee(string? UserId, string Name, string? Email)
{
    public const int MaxNameLength = 100;
    public const int MaxEmailLength = 254;

    /// <summary>
    /// Who a booking was for once the client deleted their account (user story V1-8): the
    /// business keeps the booking, but not their name, email or account.
    /// </summary>
    public static readonly Attendee Forgotten = new(null, "A former client", null);

    /// <exception cref="DomainValidationException">The name or email can't be used.</exception>
    public static Attendee Create(string? userId, string? name, string? email)
    {
        var errors = new DomainErrors();
        var trimmedName = name?.Trim();
        if (trimmedName is not { Length: > 0 and <= MaxNameLength })
        {
            errors.Add("clientName", $"Use 1 to {MaxNameLength} characters.");
        }
        var trimmedEmail = email?.Trim();
        if (trimmedEmail is not { Length: > 0 and <= MaxEmailLength } || !MailAddress.TryCreate(trimmedEmail, out _))
        {
            errors.Add("clientEmail", "Enter an email address.");
        }
        errors.ThrowIfAny();
        return new Attendee(userId, trimmedName!, trimmedEmail!);
    }
}
