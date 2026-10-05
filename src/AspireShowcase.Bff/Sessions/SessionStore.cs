using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Keeps sign-ins in bff-db instead of in the cookie, so the cookie only carries a random
/// session ID and the tokens never leave the server.
/// </summary>
/// <remarks>
/// The ticket is encrypted before it's stored: it holds the user's refresh token.
/// </remarks>
sealed class SessionStore(IServiceScopeFactory scopes, IDataProtectionProvider dataProtection, TimeProvider time)
    : ITicketStore
{
    /// <summary>
    /// The session's ID, kept in the ticket's properties: the cookie handler doesn't pass it on,
    /// and <see cref="AccessTokens"/> needs it to save refreshed tokens.
    /// </summary>
    public const string SessionIdKey = ".bff.session";

    readonly IDataProtector _protector = dataProtection.CreateProtector("bff.sessions");

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        ticket.Properties.Items[SessionIdKey] = id;

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BffDbContext>();

        // Sign-ins are rare enough to clear out expired sessions at the same time.
        var now = time.GetUtcNow();
        await db.Sessions.Where(session => session.ExpiresAt < now).ExecuteDeleteAsync();

        db.Sessions.Add(new Session
        {
            Id = id,
            UserId = ticket.Principal.FindFirst("sub")?.Value ?? "",
            Ticket = Protect(ticket),
            ExpiresAt = ExpiresAt(ticket),
        });
        await db.SaveChangesAsync();
        return id;
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        ticket.Properties.Items[SessionIdKey] = key;
        var protectedTicket = Protect(ticket);
        var expiresAt = ExpiresAt(ticket);

        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<BffDbContext>().Sessions
            .Where(session => session.Id == key)
            .ExecuteUpdateAsync(session => session
                .SetProperty(s => s.Ticket, protectedTicket)
                .SetProperty(s => s.ExpiresAt, expiresAt));
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        await using var scope = scopes.CreateAsyncScope();
        var session = await scope.ServiceProvider.GetRequiredService<BffDbContext>().Sessions
            .AsNoTracking()
            .SingleOrDefaultAsync(session => session.Id == key);
        if (session is null || session.ExpiresAt <= time.GetUtcNow())
        {
            return null;
        }

        try
        {
            return TicketSerializer.Default.Deserialize(_protector.Unprotect(session.Ticket));
        }
        catch (CryptographicException)
        {
            // Encrypted with a key that no longer exists: the user signs in again.
            return null;
        }
    }

    public async Task RemoveAsync(string key)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<BffDbContext>().Sessions
            .Where(session => session.Id == key)
            .ExecuteDeleteAsync();
    }

    byte[] Protect(AuthenticationTicket ticket) => _protector.Protect(TicketSerializer.Default.Serialize(ticket));

    DateTimeOffset ExpiresAt(AuthenticationTicket ticket) =>
        ticket.Properties.ExpiresUtc ?? time.GetUtcNow().Add(LogtoAuthentication.SessionLifetime);
}
