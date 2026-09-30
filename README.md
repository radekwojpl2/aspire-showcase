# Aspire Showcase

React + ASP.NET Core API with [Aspire](https://aspire.dev), deployed to Azure Container Apps.

```
src/
├── AspireShowcase.AppHost/          # orchestration + Azure target
├── AspireShowcase.ServiceDefaults/  # telemetry, health checks
├── AspireShowcase.Api/              # API, serves the UI in Azure
└── AspireShowcase.Web/              # React + Vite
```

## Run locally

Needs .NET 10 and Node.js 22.

```
dotnet run --project src/AspireShowcase.AppHost
```

Open the `Dashboard:` link, then `frontend`.

## Deploy

One-time setup (after `az login`, `gh auth login`):

```powershell
./scripts/setup-azure-oidc.ps1 -GitHubRepo radekwojpl2/aspire-showcase

# other region / resource group
./scripts/setup-azure-oidc.ps1 -GitHubRepo radekwojpl2/aspire-showcase -Location northeurope -ResourceGroup rg-aspire-demo
```

Then push to `main`: CI runs, and if it passes, Deploy runs.

| Workflow | Runs on |
|---|---|
| CI | PRs, pushes to `main` |
| Deploy | CI passing on `main`, manual |
| Deprovision | manual |

Tear down:

```
gh workflow run deprovision.yml -f confirm=rg-aspire-showcase
```

This keeps the resource group and its role assignments, so Deploy works again without extra steps.

> [!IMPORTANT]
> If you run `aspire destroy` instead, it deletes the resource group too, so run `./scripts/setup-azure-oidc.ps1` again before the next deploy.

## Aspire dashboard

- Local: `Dashboard:` link from `dotnet run`, e.g. `https://localhost:17019/login?t=...`
- Azure: printed after each deploy, e.g. https://aspire-dashboard.ext.wonderfulplant-84971e34.westeurope.azurecontainerapps.io
