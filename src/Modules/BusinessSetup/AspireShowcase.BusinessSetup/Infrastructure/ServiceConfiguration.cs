using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AspireShowcase.BusinessSetup;

/// <summary>How the <see cref="Service"/> aggregate is stored in app-db.</summary>
sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    // Named so a unique violation can tell which rule was broken.
    public const string NameIndex = "IX_Services_BusinessId_Name";

    public void Configure(EntityTypeBuilder<Service> service)
    {
        service.ToTable("Services");
        service.Property(s => s.Name).HasMaxLength(Service.MaxNameLength);

        // The price is a value object, stored as two columns of the service's row.
        service.ComplexProperty(s => s.Price, price =>
        {
            price.Property(p => p.Amount).HasColumnName("PriceAmount").HasPrecision(10, 2);
            price.Property(p => p.Currency).HasColumnName("PriceCurrency").HasMaxLength(3).IsFixedLength();
        });

        // Only a reference by ID: Service and Business are separate aggregates. The foreign key
        // still keeps the database consistent.
        service.HasOne<Business>().WithMany().HasForeignKey(s => s.BusinessId).OnDelete(DeleteBehavior.Restrict);

        // Names are unique per business, even when two requests race. Also serves the
        // business's list of services.
        service.HasIndex(s => new { s.BusinessId, s.Name }).IsUnique().HasDatabaseName(NameIndex);
    }
}
