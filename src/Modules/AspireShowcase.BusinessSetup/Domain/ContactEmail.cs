using System.Net.Mail;

namespace AspireShowcase.BusinessSetup;

/// <summary>
/// The email address clients can reach a business at, such as when it's too late to cancel
/// online (user story V1-3). The owner chooses it; it's not their sign-in address.
/// </summary>
static class ContactEmail
{
    public const int MaxLength = 254;

    /// <summary>Why <paramref name="email"/> can't be used, or null when it can (after trimming).</summary>
    public static string? Problem(string? email) =>
        email?.Trim() is { Length: > 0 and <= MaxLength } trimmed && MailAddress.TryCreate(trimmed, out _)
            ? null
            : "Enter an email address clients can reach you at.";
}
