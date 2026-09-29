# Aspire Showcase

A minimal [Aspire](https://aspire.dev) app with a single ASP.NET Core API, deployed to Azure Container Apps with `aspire deploy` from GitHub Actions.

```
src/
├── AspireShowcase.AppHost/          # orchestration + Azure Container Apps deployment target
├── AspireShowcase.ServiceDefaults/  # OpenTelemetry, health checks, resilience
└── AspireShowcase.Api/              # the API (/weatherforecast, /health, /alive)
```

## Run locally

```
dotnet run --project src/AspireShowcase.AppHost
```

Open the dashboard link printed in the console.

## Deploy manually

```
az login
aspire deploy --apphost src/AspireShowcase.AppHost
```

Aspire prompts for the subscription, location and resource group.

## GitHub Actions

| Workflow | Trigger | What it does |
|---|---|---|
| `ci.yml` | PRs and pushes to `main` | restore, build, test |
| `deploy.yml` | pushes to `main`, manual | `aspire deploy` to Azure Container Apps |

### One-time Azure setup (OIDC, no secrets)

1. Create an app registration and service principal:
   ```
   az ad app create --display-name aspire-showcase-github
   az ad sp create --id <appId>
   ```
2. Give it rights on the subscription. It needs **Contributor** plus **User Access Administrator**, because the deployment assigns the AcrPull role to the app's managed identity:
   ```
   az role assignment create --assignee <appId> --role Contributor --scope /subscriptions/<subscriptionId>
   az role assignment create --assignee <appId> --role "User Access Administrator" --scope /subscriptions/<subscriptionId>
   ```
3. Add a federated credential for the `production` environment:
   ```
   az ad app federated-credential create --id <appId> --parameters '{
     "name": "github-production",
     "issuer": "https://token.actions.githubusercontent.com",
     "subject": "repo:<owner>/<repo>:environment:production",
     "audiences": ["api://AzureADTokenExchange"]
   }'
   ```
4. In GitHub, create the `production` environment and add these **variables**:

   | Variable | Example |
   |---|---|
   | `AZURE_CLIENT_ID` | app registration's appId |
   | `AZURE_TENANT_ID` | your tenant id |
   | `AZURE_SUBSCRIPTION_ID` | your subscription id |
   | `AZURE_LOCATION` | `westeurope` |
   | `AZURE_RESOURCE_GROUP` | `rg-aspire-showcase` |
