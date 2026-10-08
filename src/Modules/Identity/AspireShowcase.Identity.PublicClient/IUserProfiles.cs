namespace AspireShowcase.Identity.PublicClient;

/// <summary>
/// Who a user is, for other modules: what their account says, by their subject ID (the sub
/// claim). Access tokens carry only that ID; the rest comes from here.
/// </summary>
public interface IUserProfiles
{
    /// <summary>The user's profile, or null when there's no such user.</summary>
    /// <exception cref="UserProfilesUnavailableException">The profile couldn't be read right now.</exception>
    Task<UserProfile?> FindAsync(string userId, CancellationToken cancellation);
}

/// <param name="Name">How to address them: their name, or else their username.</param>
/// <param name="Email">Their email address, when the account has one.</param>
public sealed record UserProfile(string UserId, string Name, string? Email);

/// <summary>Profiles couldn't be read: the identity provider is down, refused, or isn't set up.</summary>
public sealed class UserProfilesUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
