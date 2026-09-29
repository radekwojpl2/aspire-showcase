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
2. Open the dashboard link printed in the console, then open the `frontend` resource. Aspire runs `npm install` for you.

## Aspire dashboard

The dashboard shows every resource with its logs, traces and metrics.

### Locally

`dotnet run --project src/AspireShowcase.AppHost` prints a login link that includes a one-time token. Open that exact link:

```
Dashboard:  https://localhost:17019/login?t=c5c2626a4a673a4a00e4c1844c5e3b30
```

Opening `https://localhost:17019` without the `?t=...` part asks for the token. Copy it from the console.

### In Azure

The deployed dashboard runs inside the Container Apps environment at `https://aspire-dashboard.ext.<environment default domain>`, for example:

```
https://aspire-dashboard.ext.wonderfulplant-84971e34.westeurope.azurecontainerapps.io
```

Three ways to find it:

1. **Deploy log:** every Deploy run prints it at the end (`📊 Dashboard: https://aspire-dashboard.ext...`). Open **Actions → Deploy → latest run → Deploy with Aspire**.
2. **Azure CLI:**
   ```powershell
   $domain = az containerapp env list -g rg-aspire-showcase --query "[0].properties.defaultDomain" -o tsv
   "https://aspire-dashboard.ext.$domain"
   ```
3. **Azure portal:** **Resource groups → `rg-aspire-showcase` →** the **Container Apps Environment** (`acaenv...`) → **Overview**, which shows the default domain. Put `https://aspire-dashboard.ext.` in front of it.

Sign in with the Microsoft account you use for Azure (`az login`). The dashboard requires a Microsoft Entra ID sign-in and is not publicly readable.

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
| `deploy.yml` | CI succeeds on a push to `main`, manual | `aspire deploy` of the commit CI built, to Azure Container Apps |
| `deprovision.yml` | manual only | deletes every resource in the resource group (asks you to type its name to confirm) |

Deprovisioning keeps the resource group and the pipeline's role assignments, so running Deploy again recreates everything without re-running the setup script. Deploy and Deprovision share a concurrency group and never run at the same time.

To deprovision: **Actions → Deprovision → Run workflow**, enter the resource group name (default `rg-aspire-showcase`), or from the CLI:

```
gh workflow run deprovision.yml -f confirm=rg-aspire-showcase
```

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
