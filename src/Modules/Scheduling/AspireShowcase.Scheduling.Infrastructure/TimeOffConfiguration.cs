using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AspireShowcase.Scheduling.Infrastructure;

/// <summary>How the <see cref="TimeOff"/> aggregate is stored in app-db.</summary>
sealed class TimeOffConfiguration : IEntityTypeConfiguration<TimeOff>
{
    public void Configure(EntityTypeBuilder<TimeOff> timeOff)
    {
        timeOff.ToTable("TimeOff", table => table.HasCheckConstraint("CK_TimeOff_EndAfterStart", "\"End\" > \"Start\""));
        timeOff.Property(t => t.Note).HasMaxLength(TimeOff.MaxNoteLength);

        // The free slots' and the calendar's query: one business, a range of time.
        timeOff.HasIndex(t => new { t.BusinessId, t.End });
    }
}
