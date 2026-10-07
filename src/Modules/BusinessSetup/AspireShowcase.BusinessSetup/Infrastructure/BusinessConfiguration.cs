using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AspireShowcase.BusinessSetup;

/// <summary>How the <see cref="Business"/> aggregate is stored in app-db.</summary>
sealed class BusinessConfiguration : IEntityTypeConfiguration<Business>
{
    // Named so a unique violation can tell which rule was broken.
    public const string SlugIndex = "IX_Businesses_Slug";
    public const string OwnerIndex = "IX_Businesses_OwnerId";

    public void Configure(EntityTypeBuilder<Business> business)
    {
        business.ToTable("Businesses");
        business.Property(b => b.Name).HasMaxLength(Business.MaxNameLength);
        business.Property(b => b.Slug).HasMaxLength(BookingSlug.MaxLength);
        business.Property(b => b.OwnerId).HasMaxLength(64);
        business.Property(b => b.TimeZone).HasMaxLength(BusinessTimeZone.MaxLength);
        business.Property(b => b.ContactEmail).HasMaxLength(ContactEmail.MaxLength);
        business.Property(b => b.OpeningHours).StoredAsJson();

        // The database is what guarantees both, even when two requests race.
        business.HasIndex(b => b.Slug).IsUnique().HasDatabaseName(SlugIndex);
        business.HasIndex(b => b.OwnerId).IsUnique().HasDatabaseName(OwnerIndex);
    }
}
