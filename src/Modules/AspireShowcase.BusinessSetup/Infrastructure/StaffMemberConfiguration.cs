using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AspireShowcase.BusinessSetup;

/// <summary>How the <see cref="StaffMember"/> aggregate is stored in app-db.</summary>
sealed class StaffMemberConfiguration : IEntityTypeConfiguration<StaffMember>
{
    // Named so a unique violation can tell which rule was broken.
    public const string NameIndex = "IX_StaffMembers_BusinessId_Name";

    public void Configure(EntityTypeBuilder<StaffMember> staff)
    {
        staff.ToTable("StaffMembers");
        staff.Property(s => s.Name).HasMaxLength(StaffMember.MaxNameLength);
        staff.Property(s => s.UserId).HasMaxLength(64);

        // Null: works whenever the business is open.
        staff.Property(s => s.WorkingHours).StoredAsJson();

        // The services by ID, as a uuid[] column: they're another aggregate, and the list is only
        // ever read and replaced with the staff member.
        staff.PrimitiveCollection(s => s.ServiceIds)
            .HasField("_serviceIds")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        staff.HasOne<Business>().WithMany().HasForeignKey(s => s.BusinessId).OnDelete(DeleteBehavior.Restrict);

        // Clients choose staff by name, so names are unique per business, even when two requests
        // race. Also serves the business's list of staff.
        staff.HasIndex(s => new { s.BusinessId, s.Name }).IsUnique().HasDatabaseName(NameIndex);
    }
}
