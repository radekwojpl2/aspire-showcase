using AspireShowcase.BusinessSetup.PublicClient;
using Microsoft.EntityFrameworkCore;

namespace AspireShowcase.BusinessSetup;

/// <summary>Answers <see cref="IBusinessDirectory"/> from Business Setup's own tables.</summary>
sealed class BusinessDirectory(BusinessSetupDbContext db) : IBusinessDirectory
{
    public async Task<BusinessInfo?> FindOwnedAsync(string ownerId, CancellationToken cancellation) =>
        await db.Businesses.AsNoTracking()
            .Where(business => business.OwnerId == ownerId)
            .Select(business => new BusinessInfo(business.Id, business.Name, business.TimeZone))
            .SingleOrDefaultAsync(cancellation);

    public async Task<IReadOnlyList<BusinessInfo>> ListAsync(CancellationToken cancellation) =>
        await db.Businesses.AsNoTracking()
            .Select(business => new BusinessInfo(business.Id, business.Name, business.TimeZone))
            .ToListAsync(cancellation);

    public async Task<IReadOnlyList<StaffInfo>> StaffAsync(Guid businessId, CancellationToken cancellation)
    {
        var business = await db.Businesses.AsNoTracking().SingleOrDefaultAsync(b => b.Id == businessId, cancellation);
        if (business is null)
        {
            return [];
        }

        var staff = await db.StaffMembers.AsNoTracking()
            .Where(member => member.BusinessId == businessId)
            .OrderBy(member => member.CreatedAt)
            .ToListAsync(cancellation);
        return staff
            .Select(member => new StaffInfo(
                member.Id, member.Name, member.DoesAllServices, member.ServiceIds,
                (member.WorkingHours ?? business.OpeningHours).Periods
                    .Select(period => new WorkingPeriod(period.Day, period.Opens, period.Closes))
                    .ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<ServiceInfo>> ServicesAsync(Guid businessId, CancellationToken cancellation) =>
        await db.Services.AsNoTracking()
            .Where(service => service.BusinessId == businessId)
            .OrderBy(service => service.Name)
            .Select(service => new ServiceInfo(service.Id, service.Name, service.Duration, service.IsHidden))
            .ToListAsync(cancellation);
}
