using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AspireShowcase.Scheduling;

/// <summary>How the <see cref="CancellationPolicy"/> aggregate is stored in app-db: one row per business.</summary>
sealed class CancellationPolicyConfiguration : IEntityTypeConfiguration<CancellationPolicy>
{
    public void Configure(EntityTypeBuilder<CancellationPolicy> policy)
    {
        policy.ToTable("CancellationPolicies");
        policy.HasKey(p => p.BusinessId);
    }
}
