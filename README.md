# Aspire Showcase

A minimal [Aspire](https://aspire.dev) app: a React frontend and an ASP.NET Core API, deployed to Azure Container Apps with `aspire deploy` from GitHub Actions. In production the connection string is kept in Azure Key Vault.

```
src/
├── AspireShowcase.AppHost/          # orchestration, Azure Container Apps + Key Vault
├── AspireShowcase.ServiceDefaults/  # OpenTelemetry, health checks, resilience
├── AspireShowcase.Api/              # the API (/api/weatherforecast, /api/config-status, /health)
└── AspireShowcase.Web/              # React + Vite frontend
```

In production, the React app is built into the API container's `wwwroot`, so a single Container App serves both the UI and `/api`. Locally, Vite runs its own dev server with hot reload and forwards `/api` to the API.

## Run locally

Prerequisites: .NET 10 SDK, Node.js 22.12+ (or 20.19+), Docker (only needed for `aspire deploy`).

1. Set the connection string once in the AppHost's user secrets:
   ```
   dotnet user-secrets set "Parameters:db-connection-string" "<your dev connection string>" --project src/AspireShowcase.AppHost
   ```
   If you skip this, the Aspire dashboard prompts for the value on startup.
2. Run:
   ```
   dotnet run --project src/AspireShowcase.AppHost
   ```
3. Open the dashboard link printed in the console, then open the `web` resource. Aspire runs `npm install` for you.

## Connection string

The API always reads `ConnectionStrings:db` (`builder.Configuration.GetConnectionString("db")`). Only where the value comes from differs:

| | Source | How it reaches the API |
|---|---|---|
| Local | AppHost user secrets (`Parameters:db-connection-string`) | environment variable set by Aspire |
| Production | GitHub secret `DB_CONNECTION_STRING`, written to Key Vault secret `db-connection-string` on each deploy | Container App Key Vault reference, read through the API's managed identity (Key Vault Secrets User role) |

`/api/config-status` (shown as a badge in the UI) reports whether the value is set. It never returns the value itself.

## Deploy manually

```
az login
aspire deploy --apphost src/AspireShowcase.AppHost
```

Aspire prompts for the subscription, location, resource group and the connection string.

## GitHub Actions

| Workflow | Trigger | What it does |
|---|---|---|
| `ci.yml` | PRs and pushes to `main` | .NET restore/build/test; frontend lint + build |
| `deploy.yml` | pushes to `main`, manual | `aspire deploy` to Azure Container Apps + Key Vault |

### One-time Azure setup (OIDC, no secrets)

Log in with `az login` and `gh auth login`, then run:

```
./scripts/setup-azure-oidc.ps1 -GitHubRepo <owner>/<repo>
```

For example:

```powershell
# Defaults: rg-aspire-showcase in westeurope, current az subscription
./scripts/setup-azure-oidc.ps1 -GitHubRepo radekwojpl2/aspire-showcase

# Also store the production connection string (prompted, not echoed or kept in history)
./scripts/setup-azure-oidc.ps1 -GitHubRepo radekwojpl2/aspire-showcase -DbConnectionString (Read-Host -AsSecureString 'Connection string')

# Different region and resource group
./scripts/setup-azure-oidc.ps1 -GitHubRepo radekwojpl2/aspire-showcase -Location northeurope -ResourceGroup rg-aspire-demo

# Azure part only; prints the variables for you to set by hand
./scripts/setup-azure-oidc.ps1 -GitHubRepo radekwojpl2/aspire-showcase -SkipGitHub
```

The script can be re-run safely; anything that already exists is reused. It creates or reuses:

1. The resource group (`rg-aspire-showcase` in `westeurope` by default).
2. An Entra ID app registration and its service principal (`aspire-showcase-github`).
3. **Contributor** and **User Access Administrator** roles on that resource group. The second role is needed because the deployment grants the app's managed identity AcrPull on the registry and Key Vault Secrets User on the vault.
4. A federated credential trusting `repo:<owner>/<repo>:environment:production`.
5. The GitHub `production` environment with these variables: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_LOCATION`, `AZURE_RESOURCE_GROUP`. With `-DbConnectionString`, it also sets the `DB_CONNECTION_STRING` secret.

Options: `-SubscriptionId`, `-Location`, `-ResourceGroup`, `-AppName`, `-GitHubEnvironment`. With `-SkipGitHub`, the script only does the Azure part and prints the variables for you to set by hand.
