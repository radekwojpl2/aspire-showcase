using Microsoft.EntityFrameworkCore;

namespace AspireShowcase.BusinessSetup.Application;

/// <summary>The signed-in owner's business: everything under /businesses/mine is scoped to it.</summary>
static class Owners
{
    /// <param name="tracked">Whether the use case changes it.</param>
    public static Task<Business?> FindBusinessAsync(
        this IBusinessSetupDbContext db, string ownerId, CancellationToken cancellation, bool tracked = false) =>
        (tracked ? db.Businesses : db.Businesses.AsNoTracking())
            .SingleOrDefaultAsync(business => business.OwnerId == ownerId, cancellation);
}
