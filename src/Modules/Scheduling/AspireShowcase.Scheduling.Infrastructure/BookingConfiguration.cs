using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql;

namespace AspireShowcase.Scheduling.Infrastructure;

/// <summary>How the <see cref="Booking"/> aggregate is stored in app-db.</summary>
sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    /// <summary>
    /// The exclusion constraint that keeps a staff member's confirmed bookings, buffers included,
    /// from overlapping. EF Core can't model it, so the migrations add it in SQL.
    /// </summary>
    public const string NoOverlapConstraint = "EX_Bookings_StaffMember_NoOverlap";

    public void Configure(EntityTypeBuilder<Booking> booking)
    {
        booking.ToTable("Bookings");
        booking.Property(b => b.Status).HasConversion<string>().HasMaxLength(16);
        booking.Property(b => b.CancelledBy).HasConversion<string>().HasMaxLength(16);
        // Events aren't stored with the booking: the context moves them to the outbox on save.
        booking.Ignore(b => b.Events);

        // The attendee is a value object, stored as columns of the booking's row.
        booking.ComplexProperty(b => b.Attendee, attendee =>
        {
            attendee.Property(a => a.UserId).HasColumnName("AttendeeUserId").HasMaxLength(64);
            attendee.Property(a => a.Name).HasColumnName("AttendeeName").HasMaxLength(Attendee.MaxNameLength);
            attendee.Property(a => a.Email).HasColumnName("AttendeeEmail").HasMaxLength(Attendee.MaxEmailLength);
        });

        // The calendar's query: one business, a range of time.
        booking.HasIndex(b => new { b.BusinessId, b.Start });
    }

    /// <summary>Whether a save failed because the booking overlaps another of the same staff member.</summary>
    public static bool IsSlotTaken(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ExclusionViolation,
            ConstraintName: NoOverlapConstraint,
        };
}
