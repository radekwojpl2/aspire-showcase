# Aspire Showcase

A minimal [Aspire](https://aspire.dev) app: a React frontend and an ASP.NET Core API, deployed to Azure Container Apps with `aspire deploy` from GitHub Actions.

```
src/
├── AspireShowcase.AppHost/          # orchestration + Azure Container Apps deployment target
├── AspireShowcase.ServiceDefaults/  # OpenTelemetry, health checks, resilience
├── AspireShowcase.Api/              # the API (/api/weatherforecast, /health)
└── AspireShowcase.Web/              # React + Vite frontend
```

In production, the React app is built into the API container's `wwwroot`, so a single Container App serves both the UI and `/api`. Locally, Vite runs its own dev server with hot reload and forwards `/api` to the API.

## Run locally

Prerequisites: .NET 10 SDK, Node.js 22.12+ (or 20.19+), Docker (only needed for `aspire deploy`).

1. Run:
   ```
   dotnet run --project src/AspireShowcase.AppHost
   ```
2. Open the dashboard link printed in the console, then open the `web` resource. Aspire runs `npm install` for you.

## Deploy manually

```
az login
aspire deploy --apphost src/AspireShowcase.AppHost
```

Aspire prompts for the subscription, location and resource group.

## GitHub Actions

| Workflow | Trigger | What it does |
|---|---|---|
| `ci.yml` | PRs and pushes to `main` | .NET restore/build/test; frontend lint + build |
| `deploy.yml` | pushes to `main`, manual | `aspire deploy` to Azure Container Apps |

### One-time Azure setup (OIDC, no secrets)

Log in with `az login` and `gh auth login`, then run:

```
./scripts/setup-azure-oidc.ps1 -GitHubRepo <owner>/<repo>
```

For example:

```powershell
# Defaults: rg-aspire-showcase in westeurope, current az subscription
./scripts/setup-azure-oidc.ps1 -GitHubRepo radekwojpl2/aspire-showcase

# Different region and resource group
./scripts/setup-azure-oidc.ps1 -GitHubRepo radekwojpl2/aspire-showcase -Location northeurope -ResourceGroup rg-aspire-demo

# Azure part only; prints the variables for you to set by hand
./scripts/setup-azure-oidc.ps1 -GitHubRepo radekwojpl2/aspire-showcase -SkipGitHub
```

The script can be re-run safely; anything that already exists is reused. It creates or reuses:

1. The resource group (`rg-aspire-showcase` in `westeurope` by default).
2. An Entra ID app registration and its service principal (`aspire-showcase-github`).
3. **Contributor** and **User Access Administrator** roles on that resource group. The second role is needed because the deployment grants the app's managed identity permission to pull from the container registry (AcrPull).
4. A federated credential trusting `repo:<owner>/<repo>:environment:production`.
5. The GitHub `production` environment with these variables: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_LOCATION`, `AZURE_RESOURCE_GROUP`.

Options: `-SubscriptionId`, `-Location`, `-ResourceGroup`, `-AppName`, `-GitHubEnvironment`. With `-SkipGitHub`, the script only does the Azure part and prints the variables for you to set by hand.
