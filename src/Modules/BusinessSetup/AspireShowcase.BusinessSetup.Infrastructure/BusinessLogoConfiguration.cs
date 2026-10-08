using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AspireShowcase.BusinessSetup.Infrastructure;

/// <summary>How a <see cref="BusinessLogo"/> is stored in app-db: one row per business, deleted with it.</summary>
sealed class BusinessLogoConfiguration : IEntityTypeConfiguration<BusinessLogo>
{
    public void Configure(EntityTypeBuilder<BusinessLogo> logo)
    {
        logo.ToTable("BusinessLogos");
        logo.HasKey(l => l.BusinessId);
        logo.Property(l => l.ContentType).HasMaxLength(32);
        // A foreign key without a navigation property: the logo goes with its business.
        logo.HasOne<Business>().WithOne().HasForeignKey<BusinessLogo>(l => l.BusinessId).OnDelete(DeleteBehavior.Cascade);
    }
}
