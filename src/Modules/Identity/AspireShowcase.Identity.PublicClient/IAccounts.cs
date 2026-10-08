namespace AspireShowcase.Identity.PublicClient;

/// <summary>What other modules may ask of Identity &amp; Access about accounts, by subject ID.</summary>
public interface IAccounts
{
    /// <summary>
    /// Deletes the user's account (user story V1-8): they can't sign in any more, and their
    /// profile is gone. Deleting an account that's already gone succeeds.
    /// </summary>
    /// <exception cref="AccountsUnavailableException">It couldn't be deleted right now.</exception>
    Task DeleteAsync(string userId, CancellationToken cancellation);
}

/// <summary>Accounts couldn't be changed: the identity provider is down, refused, or isn't set up.</summary>
public sealed class AccountsUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
