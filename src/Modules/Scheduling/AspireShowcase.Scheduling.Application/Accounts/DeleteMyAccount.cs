using AspireShowcase.Identity.PublicClient;
using Microsoft.EntityFrameworkCore;

namespace AspireShowcase.Scheduling.Application.Accounts;

/// <summary>
/// A client deletes their account and their data (user story V1-8). Their bookings at every
/// business stay for those businesses, without their name, email or account; the upcoming ones
/// are cancelled, and each business hears about it. Then the account itself is deleted.
/// </summary>
/// <remarks>
/// Their data goes first: if the account can't be deleted right now, it's answered as
/// unavailable, and asking again finishes the job, as both steps can run twice. Owners are
/// refused for now: deleting a business is a story of its own.
/// </remarks>
sealed class DeleteMyAccount(
    ISchedulingDbContext db, IBookings bookings, IBusinessDirectory directory, IAccounts accounts,
    SchedulingTelemetry telemetry, TimeProvider time)
{
    public const string OwnsABusiness =
        "You own a business, so your account can't be deleted here yet. Contact us to close the business first.";

    public const string TryAgain = "Your bookings are deleted, but your account couldn't be yet. Try again in a moment.";

    public async Task<Result> HandleAsync(string userId, CancellationToken cancellation)
    {
        using var activity = telemetry.StartActivity("accounts.delete");

        if (await directory.FindOwnedAsync(userId, cancellation) is not null)
        {
            telemetry.AccountDeleted(activity, "owner");
            return Result.Conflict(OwnsABusiness);
        }

        // Every business's bookings, past and cancelled ones too: the one other query that
        // crosses businesses, scoped to the client's own user ID.
        var now = time.GetUtcNow();
        var theirs = await db.OfClient(userId).ToListAsync(cancellation);
        var cancelled = 0;
        foreach (var booking in theirs)
        {
            booking.ForgetClient(now);
            cancelled += booking.Events.Count;
            // Each through the outbox, so businesses hear about the ones cancelled.
            await bookings.SaveAsync(booking, cancellation);
        }

        try
        {
            await accounts.DeleteAsync(userId, cancellation);
        }
        catch (AccountsUnavailableException)
        {
            telemetry.AccountDeleted(activity, "unavailable", theirs.Count, cancelled);
            return Result.Unavailable(TryAgain);
        }

        telemetry.AccountDeleted(activity, "deleted", theirs.Count, cancelled);
        return Result.Done();
    }
}
