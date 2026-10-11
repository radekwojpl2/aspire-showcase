using AspireShowcase.Identity.PublicClient;

namespace AspireShowcase.Scheduling.Application.Accounts;

/// <summary>
/// Who's signed in: their name and email from their account, and the business they own, if any.
/// For clients such as the MCP server, whose tokens carry only the user's ID.
/// </summary>
sealed class GetMe(IUserProfiles profiles, IBusinessDirectory directory)
{
    public const string TryAgain = "Your account couldn't be read right now. Try again in a moment.";

    public async Task<Result<Me>> HandleAsync(string userId, CancellationToken cancellation)
    {
        UserProfile? profile;
        try
        {
            profile = await profiles.FindAsync(userId, cancellation);
        }
        catch (UserProfilesUnavailableException)
        {
            return Result.Unavailable(TryAgain);
        }

        // A token can outlive its account, which was deleted in the meantime.
        if (profile is null)
        {
            return Result.NotFound();
        }

        var business = await directory.FindOwnedAsync(userId, cancellation);
        return Result.Done(new Me(
            profile.Name, profile.Email, business is null ? null : new OwnedBusiness(business.Name, business.Slug)));
    }
}
