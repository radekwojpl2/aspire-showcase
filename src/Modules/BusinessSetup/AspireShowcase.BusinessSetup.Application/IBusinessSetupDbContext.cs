using Microsoft.EntityFrameworkCore;

namespace AspireShowcase.BusinessSetup.Application;

/// <summary>What the use cases need of Business Setup's storage; Infrastructure's DbContext provides it.</summary>
interface IBusinessSetupDbContext
{
    DbSet<Business> Businesses { get; }

    DbSet<Service> Services { get; }

    DbSet<StaffMember> StaffMembers { get; }

    /// <exception cref="AlreadyExistsException">A unique rule was broken, such as a taken booking link.</exception>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="work"/> in one transaction, which is committed if it doesn't throw.
    /// Transient database failures retry the whole of it, so it has to add its changes itself.
    /// </summary>
    Task InTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellation);
}

/// <summary>The unique rules of Business Setup's data, which the database enforces even when requests race.</summary>
enum UniqueRule
{
    /// <summary>A booking link is used by one business only.</summary>
    Slug,

    /// <summary>An owner has one business only.</summary>
    Owner,

    /// <summary>A business's services have different names.</summary>
    ServiceName,

    /// <summary>A business's staff members have different names.</summary>
    StaffName,
}

/// <summary>Saving would break a unique rule: someone already has it.</summary>
sealed class AlreadyExistsException(UniqueRule rule, Exception inner)
    : Exception($"Saving would break the unique rule {rule}.", inner)
{
    public UniqueRule Rule { get; } = rule;
}
