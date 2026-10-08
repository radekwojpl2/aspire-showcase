using AspireShowcase.Identity.Infrastructure;
using AspireShowcase.Identity.PublicClient;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AspireShowcase.Identity;

/// <summary>
/// Identity &amp; Access: the API's anti-corruption layer over Logto. Other modules see users as a
/// subject ID, the <see cref="Policies.Owner"/> policy, <see cref="IOwnerRoles"/>,
/// <see cref="IUserProfiles"/> and <see cref="IAccounts"/>, all in Identity.PublicClient; only this
/// module knows about Logto's tokens, scopes and Management API.
/// </summary>
public static class IdentityAccess
{
    /// <summary>The permission of the API resource that Logto's owner role grants.</summary>
    const string ManageBusinessScope = "manage:business";

    public static void AddIdentityAccess(this IHostApplicationBuilder builder)
    {
        // Accepts Logto access tokens issued for this API's resource, which bff adds to the requests
        // it forwards. Logto's issuer is its public URL + /oidc, and the signing keys come from its
        // discovery document there.
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                var logtoEndpoint = builder.Configuration["Logto:Endpoint"];
                if (!string.IsNullOrEmpty(logtoEndpoint))
                {
                    options.Authority = $"{logtoEndpoint}/oidc";
                }
                options.Audience = builder.Configuration["Logto:ApiResource"];
                // Locally Logto is served over plain HTTP.
                options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
                // Keep claim names as Logto sends them (sub, scope, client_id).
                options.MapInboundClaims = false;
            });

        // Logto puts the permissions the user's roles grant in the scope claim, space-separated.
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Owner, policy => policy.RequireAssertion(context =>
                context.User.FindFirst("scope")?.Value.Split(' ').Contains(ManageBusinessScope) == true));

        // Logto's Management API, for giving owners their role. Called as the machine-to-machine
        // application the AppHost passes in; Logto's public URL is its address.
        var management = builder.Configuration.GetSection("Logto").Get<LogtoManagementSettings>() ?? new();
        builder.Services.AddSingleton(management);
        builder.Services.AddHttpClient<LogtoManagement>(client =>
        {
            if (!string.IsNullOrEmpty(management.Endpoint))
            {
                client.BaseAddress = new Uri($"{management.Endpoint}/");
            }
        });
        builder.Services.AddTransient<IOwnerRoles>(services => services.GetRequiredService<LogtoManagement>());
        builder.Services.AddTransient<IUserProfiles>(services => services.GetRequiredService<LogtoManagement>());
        builder.Services.AddTransient<IAccounts>(services => services.GetRequiredService<LogtoManagement>());
    }
}
