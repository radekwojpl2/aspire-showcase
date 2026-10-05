using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// bff's own database: the sessions of signed-in browsers, and the data protection keys that
/// encrypt them and the session cookie. Both have to outlive a restart and be shared by every
/// replica, or users would be signed out.
/// </summary>
sealed class BffDbContext(DbContextOptions<BffDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Session>(session =>
        {
            session.Property(s => s.Id).HasMaxLength(64);
            session.Property(s => s.UserId).HasMaxLength(64);
            // To sign a user out everywhere, and to clear out expired sessions.
            session.HasIndex(s => s.UserId);
            session.HasIndex(s => s.ExpiresAt);
        });
}

/// <summary>A signed-in browser, which holds only its <see cref="Id"/> in the session cookie.</summary>
sealed class Session
{
    public required string Id { get; set; }

    /// <summary>The Logto user ID (sub).</summary>
    public required string UserId { get; set; }

    /// <summary>The authentication ticket with the user's tokens, encrypted with data protection.</summary>
    public required byte[] Ticket { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}
