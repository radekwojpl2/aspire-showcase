using Aspire.Hosting.Azure;

/// <summary>The PostgreSQL server, and in Azure the Key Vault its connection strings are kept in.</summary>
sealed record PostgresServer(
    IResourceBuilder<AzurePostgresFlexibleServerResource> Server,
    IResourceBuilder<AzureKeyVaultResource>? KeyVault);

static class PostgresExtensions
{
    /// <summary>
    /// One PostgreSQL server for both databases, Logto's and the app's. Locally it runs in a
    /// container with its data in a volume, so the data survives restarts; in Azure it's a
    /// Flexible Server.
    /// </summary>
    /// <remarks>
    /// It uses a user name and password, because Logto can't sign in with Entra ID. The password
    /// is generated once and kept in user secrets locally; Deploy passes it from a GitHub secret.
    /// </remarks>
    public static PostgresServer AddPostgresServer(this IDistributedApplicationBuilder builder)
    {
        // Named after Logto, its first user. An existing server's admin login can't be changed.
        var userName = builder.AddParameter("postgres-username", "logto_admin");
        var password = builder.AddParameter(
            "postgres-password",
            // No special characters: the password goes into a postgresql:// URL.
            new GenerateParameterDefault { MinLength = 32, Special = false },
            secret: true,
            persist: true);

        var postgres = builder.AddAzurePostgresFlexibleServer("postgres");

        // Azure only: connection strings are kept in Key Vault, and the Container Apps read
        // them from there with their managed identities instead of holding the password.
        var keyVault = builder.ExecutionContext.IsPublishMode ? builder.AddAzureKeyVault("kv") : null;
        if (keyVault is not null)
        {
            postgres.WithPasswordAuthentication(keyVault, userName, password);
        }
        else
        {
            postgres.WithPasswordAuthentication(userName, password);
        }

        postgres.RunAsContainer(container => container.WithDataVolume("aspireshowcase-postgres-data"));

        return new PostgresServer(postgres, keyVault);
    }

    /// <summary>
    /// Adds the app's own database to the server and gives the API its connection string
    /// (ConnectionStrings__app-db), which the API's EF Core context reads.
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithAppDatabase(
        this IResourceBuilder<ProjectResource> web, PostgresServer postgres)
    {
        var database = postgres.Server.AddDatabase("app-db", databaseName: "app");

        return web.WithReference(database).WaitFor(database);
    }
}
