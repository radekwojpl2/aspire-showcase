using Azure.Provisioning;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.ApplicationInsights;
using Azure.Provisioning.ContainerRegistry;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.KeyVault;
using Azure.Provisioning.OperationalInsights;
using Azure.Provisioning.PostgreSql;
using Azure.Provisioning.Primitives;
using Azure.Provisioning.Roles;

/// <summary>
/// Names the Azure resources "showcase-{kind}-{suffix}" (e.g. showcase-kv-ige35) instead of
/// Aspire's "{name}-{13-character hash}", so they're recognizable in the portal.
/// </summary>
/// <remarks>
/// The suffix is the first 5 characters of a hash of the resource group, so the names stay the
/// same on every deploy and still differ from other people's: Key Vault, Container Registry and
/// PostgreSQL names must be unique across Azure. Container Apps keep their own names (web, logto),
/// and resources that are only referenced (existing) keep the name they're looked up by.
/// </remarks>
sealed class AzureResourceNames : InfrastructureResolver
{
    const string Prefix = "showcase";

    public override void ResolveProperties(ProvisionableConstruct construct, ProvisioningBuildOptions options)
    {
        if (construct is ProvisionableResource { IsExistingResource: false })
        {
            switch (construct)
            {
                case KeyVaultService vault:
                    // Key Vault names are at most 24 characters.
                    vault.Name = Name("kv");
                    break;
                case ContainerRegistryService registry:
                    // Registry names allow letters and digits only.
                    registry.Name = BicepFunction.Interpolate($"{Prefix}acr{Suffix()}");
                    break;
                case PostgreSqlFlexibleServer server:
                    server.Name = Name("postgres");
                    break;
                case OperationalInsightsWorkspace workspace:
                    workspace.Name = Name("logs");
                    break;
                case ApplicationInsightsComponent insights:
                    insights.Name = Name("insights");
                    break;
                case ContainerAppManagedEnvironment environment:
                    environment.Name = Name("aca-env");
                    break;
                case UserAssignedIdentity identity:
                    // One per app, named after its Bicep identifier (logto_identity -> logto-identity).
                    identity.Name = Name(identity.BicepIdentifier.Replace('_', '-'));
                    break;
            }
        }

        base.ResolveProperties(construct, options);
    }

    static BicepValue<string> Name(string kind) => BicepFunction.Interpolate($"{Prefix}-{kind}-{Suffix()}");

    static BicepExpression Suffix() =>
        BicepFunction.Take(BicepFunction.GetUniqueString(BicepFunction.GetResourceGroup().Id), 5).Compile();
}
