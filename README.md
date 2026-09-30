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

Needs .NET 10, Node.js 22 and Docker (for PostgreSQL and Logto).

```
dotnet run --project src/AspireShowcase.AppHost
```

Open the `Dashboard:` link, then `frontend`.

## Logto

[Logto](https://logto.io) handles authentication. It runs as two containers from the same image, sharing one database:

| Resource | Port | Serves |
|---|---|---|
| `logto` | 3001 | Sign-in and OIDC endpoints (`ENDPOINT`). Seeds the database on first start. |
| `logto-admin` | 3002 | Admin console at `/console` (`ADMIN_ENDPOINT`) |

Two containers, because a Container App has only one HTTP ingress port.

Its database, `logto`, lives on the `postgres` resource:

- Local: a PostgreSQL container with its data in a Docker volume, so users and settings survive restarts. The password is generated on first run and saved in the AppHost's user secrets.
- Azure: an Azure Database for PostgreSQL Flexible Server (Burstable B1ms, password auth, TLS). The admin password comes from the `POSTGRES_PASSWORD` secret on the `production` environment, which `setup-azure-oidc.ps1` generates.
  The connection URL is stored in Key Vault (`kv`) as the `logto-db-url` secret. The Logto Container Apps read it from there with their managed identities (Key Vault Secrets User), so the password is not in their configuration.

The first time, open the `Admin console` link on the `logto-admin` resource and create the admin account. In Azure, do it right after the first deploy: whoever opens the console first becomes the admin.

Locally the console is served on `http://127.0.0.1:<port>/console`, not `localhost`: Logto runs in production mode, which blocks the console's API calls from a `localhost` address. Use the link from the dashboard.

### Set up Logto for the React app and API

Do this once in the local admin console: start the AppHost and open the `Admin console` link on the `logto-admin` resource.

**React app**

1. **Applications** → **Create application** → **Single page app** → React.
2. On the application, add:
   - **Redirect URIs**: `http://localhost:5173/callback`
   - **Post sign-out redirect URIs**: `http://localhost:5173`
3. Copy its **App ID** into the AppHost's user secrets and restart the AppHost:

   ```
   dotnet user-secrets set Parameters:logto-app-id <app-id> --project src/AspireShowcase.AppHost
   ```

**API**

1. **API resources** → **Create API resource**: any name, identifier `https://api.aspire-showcase`. It must match `ApiResource` in `LogtoExtensions.cs`, which the AppHost passes to the API.

**User**

1. **User management** → create a user to sign in with. The admin account can't sign in to the app.

Then open `http://localhost:5173`, click **Sign in**, and **Call /api/me** on the **Protected endpoint** card.

## Deploy

One-time setup (after `az login`, `gh auth login`):

```powershell
./scripts/setup-azure-oidc.ps1 -GitHubRepo radekwojpl2/aspire-showcase

# other region / resource group
./scripts/setup-azure-oidc.ps1 -GitHubRepo radekwojpl2/aspire-showcase -Location northeurope -ResourceGroup rg-aspire-demo
```

If you already ran it before Logto was added, run it again: it adds the `POSTGRES_PASSWORD` secret and leaves everything else as is.

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

## Application Insights workbook

Deploy publishes **Aspire showcase overview** to `insights` → Workbooks, with a time range picker and four tabs:

| Tab | Shows |
|---|---|
| Requests | Request rate and failures, latency, endpoints, failed requests, outgoing calls (server and browser) |
| Metrics | A picker for any OpenTelemetry metric, HTTP server/client, GC heap, thread pool, all metrics |
| Browser | Page views, page load time, pages, browser exceptions |
| Logs & exceptions | Logs by severity, exceptions, warnings and errors, exceptions by type, recent logs |

It is defined in `src/AspireShowcase.AppHost/workbooks`:

- `overview.workbook.json` holds the layout and queries. `__APPINSIGHTS_ID__` is replaced with the Application Insights resource ID at deploy time.
- `overview.bicep` creates the workbook. The AppHost adds it with `AddBicepTemplate`, in publish mode only.

To change it, edit `overview.workbook.json` and push. Deploys overwrite changes made in the portal. To design a change in the portal instead, edit the workbook there, copy the JSON from Edit → Advanced Editor → Gallery Template into `overview.workbook.json`, set `fallbackResourceIds` back to `["__APPINSIGHTS_ID__"]`, and push.

## Aspire dashboard

- Local: `Dashboard:` link from `dotnet run`, e.g. `https://localhost:17019/login?t=...`
- Azure: printed after each deploy, e.g. https://aspire-dashboard.ext.wonderfulplant-84971e34.westeurope.azurecontainerapps.io
