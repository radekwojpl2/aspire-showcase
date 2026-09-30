using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.KeyVault;
using Azure.Provisioning.PostgreSql;
using Microsoft.Extensions.DependencyInjection;

var builder = DistributedApplication.CreateBuilder(args);

// Readable Azure resource names (showcase-kv-ige35) instead of Aspire's hashed ones.
builder.Services.Configure<AzureProvisioningOptions>(options =>
    options.ProvisioningBuildOptions.InfrastructureResolvers.Insert(0, new AzureResourceNames()));

// Deployment target for `aspire deploy`: an Azure Container Apps environment
// (with its own container registry).
var acaEnv = builder.AddAzureContainerAppEnvironment("aca-env");

// The ASP.NET Core project serves both /api and, once published, the React UI,
// so it is deployed as the "web" Container App.
var web = builder.AddProject<Projects.AspireShowcase_Api>("web")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    // Link on the dashboard; Swagger UI is only mapped in Development.
    .WithUrl("/swagger", "Swagger");

// Azure only: keeps logs, traces and metrics in Application Insights.
// Locally, telemetry goes to the Aspire dashboard as before, and nothing here
// is provisioned (run mode would otherwise try to create these in Azure).
if (builder.ExecutionContext.IsPublishMode)
{
    // One Log Analytics workspace shared by the Container Apps environment and
    // Application Insights; otherwise each creates its own.
    var logs = builder.AddAzureLogAnalyticsWorkspace("logs");
    acaEnv.WithAzureLogAnalyticsWorkspace(logs);

    var insights = builder.AddAzureApplicationInsights("insights")
        .WithLogAnalyticsWorkspace(logs);
    web.WithReference(insights);

    // Shared workbook with requests, metrics, browser telemetry and logs,
    // listed under Workbooks in Application Insights.
    builder.AddBicepTemplate("dashboards", "workbooks/overview.bicep")
        .WithParameter("appInsightsName", insights.Resource.NameOutputReference)
        .WithParameter("serializedData", File.ReadAllText(
            Path.Combine(builder.AppHostDirectory, "workbooks", "overview.workbook.json")));
}

// PostgreSQL for Logto. Locally it runs in a container with its data in a volume,
// so users and Logto settings survive restarts; in Azure it's a Flexible Server.
// Logto connects with a user name and password (it can't use Entra ID). The password is
// generated once and kept in user secrets locally; Deploy passes it from a GitHub secret.
var postgresUserName = builder.AddParameter("postgres-username", "logto_admin");
var postgresPassword = builder.AddParameter(
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
    postgres.WithPasswordAuthentication(keyVault, postgresUserName, postgresPassword);
}
else
{
    postgres.WithPasswordAuthentication(postgresUserName, postgresPassword);
}

postgres.RunAsContainer(container => container.WithDataVolume("aspireshowcase-postgres-data"));
var logtoDb = postgres.AddDatabase("logto-db", databaseName: "logto");

// Logto wants a postgresql:// URL, while the connection strings Aspire puts in Key Vault use
// the Npgsql format, so the URL is stored as its own secret. It's written by the server's
// Bicep, next to those connection strings: adding it with keyVault.AddSecret would make the
// vault depend on the server's host name while the server depends on the vault.
const string logtoDbUrlSecretName = "logto-db-url";
if (keyVault is not null)
{
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
            Name = logtoDbUrlSecretName,
            Properties = new SecretProperties
            {
                // Azure Database for PostgreSQL only accepts TLS connections.
                Value = BicepFunction.Interpolate(
                    $"postgresql://{login}:{password}@{server.FullyQualifiedDomainName}/{logtoDb.Resource.DatabaseName}?sslmode=require"),
            },
        });
    });
}

// Logto serves sign-in on port 3001 and its admin console on port 3002. A Container App
// has a single HTTP ingress port, so each port gets its own container, both running the
// same image against the same database. The first one creates the database schema.
IResourceBuilder<ContainerResource> AddLogto(string name, int port)
{
    var container = builder.AddContainer(name, "svhd/logto", "1.44")
        .WithHttpEndpoint(targetPort: port, name: "http")
        .WithExternalHttpEndpoints()
        // TLS ends at the Container Apps ingress; trust its X-Forwarded-* headers.
        .WithEnvironment("TRUST_PROXY_HEADER", "1")
        .WaitFor(logtoDb);

    return keyVault is not null
        ? container
            .WithEnvironment("DB_URL", keyVault.GetSecret(logtoDbUrlSecretName))
            // Logto doesn't read this. It uses an output of the server's deployment, so the
            // Container App is deployed after it; that deployment is also what writes
            // logto-db-url, which a reference to the vault alone doesn't wait for.
            .WithEnvironment("POSTGRES_HOST", postgres.GetOutput("hostName"))
        : container.WithEnvironment("DB_URL", logtoDb.Resource.UriExpression);
}

var logto = AddLogto("logto", 3001)
    .WithEntrypoint("sh")
    .WithArgs("-c", "npm run cli db seed -- --swe && npm start")
    // /api/status answers 204 No Content when Logto is up.
    .WithHttpHealthCheck("/api/status", statusCode: 204);

var logtoAdmin = AddLogto("logto-admin", 3002)
    .WaitFor(logto);

// Both need both public URLs: ENDPOINT for sign-in, ADMIN_ENDPOINT for the console.
// Browsers open these, so locally they must be localhost addresses; a container would
// otherwise get addresses on the container network (http://logto.dev.internal:3001).
EndpointReference PublicEndpoint(IResourceBuilder<ContainerResource> resource) =>
    builder.ExecutionContext.IsRunMode
        ? resource.GetEndpoint("http", KnownNetworkIdentifiers.LocalhostNetwork)
        : resource.GetEndpoint("http");

// Logto runs with NODE_ENV=production, which refuses CORS from an admin console whose
// hostname is "localhost", so the console can't call the Management API. Locally it's
// served on 127.0.0.1 instead, which the check lets through.
var adminEndpoint = builder.ExecutionContext.IsRunMode
    ? ReferenceExpression.Create($"http://127.0.0.1:{PublicEndpoint(logtoAdmin).Property(EndpointProperty.Port)}")
    : ReferenceExpression.Create($"{PublicEndpoint(logtoAdmin)}");
logtoAdmin.WithUrl(ReferenceExpression.Create($"{adminEndpoint}/console"), "Admin console");

foreach (var instance in new[] { logto, logtoAdmin })
{
    instance
        .WithEnvironment("ENDPOINT", PublicEndpoint(logto))
        .WithEnvironment("ADMIN_ENDPOINT", adminEndpoint);
}

// Vite dev server with hot reload, used for local development only.
var frontend = builder.AddViteApp("frontend", "../AspireShowcase.Web")
    .WithReference(web)
    .WaitFor(web);

// On publish, the built React app is copied into the web container's wwwroot,
// so a single Container App serves both the UI and /api.
web.PublishWithContainerFiles(frontend, "wwwroot");

builder.Build().Run();
