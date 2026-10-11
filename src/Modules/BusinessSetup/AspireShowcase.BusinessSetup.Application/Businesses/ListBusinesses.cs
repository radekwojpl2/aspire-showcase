using AspireShowcase.BusinessSetup.PublicClient;
using Microsoft.EntityFrameworkCore;

namespace AspireShowcase.BusinessSetup.Application.Businesses;

/// <summary>
/// Every business, for anyone to find one to book with, by name: what each booking page shows
/// at the top. A booking page is public, so listing them is too.
/// </summary>
sealed class ListBusinesses(IBusinessSetupDbContext db)
{
    /// <param name="query">Text the name or description has in it, whatever the case; null for every business.</param>
    public async Task<IReadOnlyList<BusinessListing>> HandleAsync(string? query, CancellationToken cancellation)
    {
        var businesses = db.Businesses.AsNoTracking();
        if (query?.Trim().ToLowerInvariant() is { Length: > 0 } text)
        {
            businesses = businesses.Where(business => business.Name.ToLower().Contains(text)
                || (business.Description != null && business.Description.ToLower().Contains(text)));
        }

        var found = await businesses
            .OrderBy(business => business.Name)
            .Select(business => new { business.Name, business.Slug, business.Address, business.Description, business.LogoVersion })
            .ToListAsync(cancellation);
        return found
            .Select(business => new BusinessListing(
                business.Name, business.Slug, business.Address, business.Description,
                BusinessInfo.LogoUrlOf(business.Slug, business.LogoVersion)))
            .ToList();
    }
}
