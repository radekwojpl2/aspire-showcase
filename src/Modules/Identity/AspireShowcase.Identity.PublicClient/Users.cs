using System.Security.Claims;

namespace AspireShowcase.Identity.PublicClient;

/// <summary>
/// Who's signed in, for other modules: a user is their subject ID. Only Identity knows that it's
/// Logto's sub claim.
/// </summary>
public static class Users
{
    /// <summary>The signed-in user's ID, for an endpoint that requires sign-in.</summary>
    /// <exception cref="InvalidOperationException">The user isn't signed in with an access token for this API.</exception>
    public static string IdOf(ClaimsPrincipal user) =>
        user.FindFirst("sub")?.Value ?? throw new InvalidOperationException("The access token has no sub claim.");
}

/// <summary>The authorization policies Identity sets up, by name, for endpoints to require.</summary>
public static class Policies
{
    /// <summary>For owners: users with Logto's owner role, which comes with starting a business.</summary>
    public const string Owner = "owner";
}
