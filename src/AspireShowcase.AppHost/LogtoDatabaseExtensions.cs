using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.KeyVault;
using Azure.Provisioning.PostgreSql;

/// <summary>Logto's database, and in Azure the Key Vault its connection URL is kept in.</summary>
sealed record LogtoDatabase(
    IResourceBuilder<AzurePostgresFlexibleServerResource> Server,
    IResourceBuilder<AzurePostgresFlexibleServerDatabaseResource> Database,
    IResourceBuilder<AzureKeyVaultResource>? KeyVault);

static class LogtoDatabaseExtensions
{
    // Logto wants a postgresql:// URL, while the connection strings Aspire puts in Key Vault use
    // the Npgsql format, so the URL is stored as its own secret.
    const string DbUrlSecretName = "logto-db-url";

    /// <summary>
    /// PostgreSQL for Logto. Locally it runs in a container with its data in a volume,
    /// so users and Logto settings survive restarts; in Azure it's a Flexible Server.
    /// </summary>
    /// <remarks>
    /// Logto connects with a user name and password (it can't use Entra ID). The password is
    /// generated once and kept in user secrets locally; Deploy passes it from a GitHub secret.
    /// </remarks>
    public static LogtoDatabase AddLogtoDatabase(this IDistributedApplicationBuilder builder)
    {
        var userName = builder.AddParameter("postgres-username", "logto_admin");
        var password = builder.AddParameter(
            "postgres-password",
            // No special characters: the password goes into a postgresql:// URL.
            new GenerateParameterDefault { MinLength = 32, Special = false },
            secret: true,
            persist: true);

        var postgres = builder.AddAzurePostgresFlexibleServer("postgres");

        // Azure only: connection strings are kept in Key Vault, and the Logto Container Apps
        // read DB_URL from there with their managed identities instead of holding the password.
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
        var database = postgres.AddDatabase("logto-db", databaseName: "logto");

        if (keyVault is not null)
        {
            WriteDbUrlSecret(postgres, database.Resource.DatabaseName);
        }

        return new LogtoDatabase(postgres, database, keyVault);
    }

    /// <summary>Gives a Logto container its DB_URL and waits for the database.</summary>
    public static IResourceBuilder<ContainerResource> WithLogtoDatabase(
        this IResourceBuilder<ContainerResource> container, LogtoDatabase db)
    {
        container.WaitFor(db.Database);

        return db.KeyVault is not null
            ? container
                .WithEnvironment("DB_URL", db.KeyVault.GetSecret(DbUrlSecretName))
                // Logto doesn't read this. It uses an output of the server's deployment, so the
                // Container App is deployed after it; that deployment is also what writes
                // logto-db-url, which a reference to the vault alone doesn't wait for.
                .WithEnvironment("POSTGRES_HOST", db.Server.GetOutput("hostName"))
            : container.WithEnvironment("DB_URL", db.Database.Resource.UriExpression);
    }

    // The secret is written by the server's Bicep, next to the connection strings: adding it with
    // keyVault.AddSecret would make the vault depend on the server's host name while the server
    // depends on the vault.
    static void WriteDbUrlSecret(IResourceBuilder<AzurePostgresFlexibleServerResource> postgres, string databaseName) =>
        postgres.ConfigureInfrastructure(infra =>
        {
            var resources = infra.GetProvisionableResources();
            var server = resources.OfType<PostgreSqlFlexibleServer>().Single();
            var vault = resources.OfType<KeyVaultService>().Single();
            var parameters = resources.OfType<ProvisioningParameter>().ToList();
            var login = parameters.Single(p => p.BicepIdentifier == "administratorLogin");
            var password = parameters.Single(p => p.BicepIdentifier == "administratorLoginPassword");

            infra.Add(new KeyVaultSecret("logtoDbUrl")
            {
                Parent = vault,
                Name = DbUrlSecretName,
                Properties = new SecretProperties
                {
                    // Azure Database for PostgreSQL only accepts TLS connections.
                    Value = BicepFunction.Interpolate(
                        $"postgresql://{login}:{password}@{server.FullyQualifiedDomainName}/{databaseName}?sslmode=require"),
                },
            });
        });
}
