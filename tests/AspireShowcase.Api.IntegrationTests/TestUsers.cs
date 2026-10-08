using System.Collections.Concurrent;
using System.Security.Cryptography;
using AspireShowcase.Identity.PublicClient;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AspireShowcase.Api.IntegrationTests;

/// <summary>A Logto user, new for every test.</summary>
/// <param name="IsOwner">Has the owner role, whose manage:business permission Logto puts in the token.</param>
public sealed record TestUser(string Id, bool IsOwner)
{
    public static TestUser Client() => new($"client-{Guid.NewGuid():N}", false);

    public static TestUser Owner() => new($"owner-{Guid.NewGuid():N}", true);
}

/// <summary>
/// Access tokens shaped like Logto's (sub, and the permissions in scope), signed with a key only
/// the tests know. The host's JwtBearer scheme and owner policy check them as they would Logto's.
/// </summary>
static class TestTokens
{
    const string Issuer = "https://logto.test/oidc";
    const string Audience = "https://api.test";

    static readonly SymmetricSecurityKey Key = new(RandomNumberGenerator.GetBytes(32));

    public static string For(TestUser user) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer,
        Audience = Audience,
        Expires = DateTime.UtcNow.AddHours(1),
        SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256),
        Claims = new Dictionary<string, object>
        {
            ["sub"] = user.Id,
            ["scope"] = user.IsOwner ? "openid manage:business" : "openid",
        },
    });

    /// <summary>Makes the host trust the test key instead of Logto's signing keys.</summary>
    public static void Accept(IServiceCollection services) =>
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = Issuer,
                ValidAudience = Audience,
                IssuerSigningKey = Key,
            });
}

/// <summary>Logto's Management API giving the owner role, or failing to, as a test asks.</summary>
public sealed class FakeOwnerRoles : IOwnerRoles
{
    readonly ConcurrentDictionary<string, bool> _owners = new();
    readonly ConcurrentDictionary<string, bool> _failing = new();

    public bool IsOwner(string userId) => _owners.ContainsKey(userId);

    /// <summary>Logto is down for this user from now on.</summary>
    public void FailFor(string userId) => _failing[userId] = true;

    public Task AssignOwnerRoleAsync(string userId, CancellationToken cancellation)
    {
        if (_failing.ContainsKey(userId))
        {
            throw new OwnerRoleUnavailableException("Logto is down in this test.");
        }
        _owners[userId] = true;
        return Task.CompletedTask;
    }
}

/// <summary>Every user exists, with an email made from their ID.</summary>
public sealed class FakeUserProfiles : IUserProfiles
{
    public static string EmailOf(string userId) => $"{userId}@example.com";

    public Task<UserProfile?> FindAsync(string userId, CancellationToken cancellation) =>
        Task.FromResult<UserProfile?>(new UserProfile(userId, $"User {userId}", EmailOf(userId)));
}
