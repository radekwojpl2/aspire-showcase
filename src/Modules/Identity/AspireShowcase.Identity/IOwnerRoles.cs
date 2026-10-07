namespace AspireShowcase.Identity;

/// <summary>
/// What other modules may ask of Identity &amp; Access about roles. Users are their subject ID
/// (the sub claim); how roles are kept is Identity's business.
/// </summary>
public interface IOwnerRoles
{
    /// <summary>Makes the user an owner, unless they are already. Safe to call twice.</summary>
    /// <exception cref="OwnerRoleUnavailableException">The role couldn't be given right now.</exception>
    Task AssignOwnerRoleAsync(string userId, CancellationToken cancellation);
}

/// <summary>The owner role couldn't be given: the identity provider is down, refused, or isn't set up.</summary>
public sealed class OwnerRoleUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
