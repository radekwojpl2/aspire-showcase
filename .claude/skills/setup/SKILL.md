---
name: setup
description: Set up the Aspire showcase on this machine - check the prerequisites, start the app, guide the user through the Logto sign-in setup and verify it works. Use when someone wants to run the project for the first time, or when sign-in or the to-do list is missing.
---

# Set up the Aspire showcase

Walk the user through a first run. Do the mechanical steps yourself and stop only where a person is needed (the Logto console). Say briefly what each step did before moving on. Never print secrets.

## 1. Check the prerequisites

Run these and compare:

| Tool | Check | Needed |
|---|---|---|
| .NET SDK | `dotnet --version` | 10, matching `global.json` |
| Node.js | `node --version` | 22 |
| Docker | `docker info` | installed and running |
| Aspire CLI | `aspire --version` | installed; if not: `dotnet tool install --global Aspire.Cli` |
| Azure CLI | `az version` | for the Azure part only |

Stop and tell the user what to install if .NET, Docker or the Aspire CLI is missing, or Docker isn't running. An older Node.js or a missing Azure CLI is a warning: say so and carry on.

## 2. Start the app

```
aspire start
aspire wait web
aspire wait logto
```

The first start pulls container images and installs npm packages, so it can take a few minutes. Give the user the dashboard link that `aspire start` prints. If a resource doesn't come up, read `aspire describe` and `aspire logs <resource>` and fix that before going on.

## 3. Is sign-in already set up?

```
curl -skL http://localhost:5268/api/config
```

`-kL` because the API redirects to HTTPS with a development certificate. If `logtoAppId` has a value, skip to step 6.

## 4. Logto setup (the user does this in a browser)

Run `aspire wait logto-admin --status up`, then get the `Admin console` URL of the `logto-admin` resource from `aspire describe`. It must be the `http://127.0.0.1:<port>/console` one; the console doesn't work on `localhost`.

Give the user these steps, with the values filled in, and wait for the App ID:

1. Open the admin console and create the admin account.
2. **Applications** → **Create application** → **Single page app** → React. On it, add:
   - **Redirect URIs**: `http://localhost:5173/callback`
   - **Post sign-out redirect URIs**: `http://localhost:5173`
3. **API resources** → **Create API resource**: any name, identifier `https://api.aspire-showcase`.
4. **User management** → create a user with a password. The admin account can't sign in to the app.
5. Copy the application's **App ID** and paste it here.

## 5. Apply the App ID

```
dotnet user-secrets set Parameters:logto-app-id <app-id> --project src/AspireShowcase.AppHost
aspire stop
aspire start
aspire wait web
```

## 6. Verify

- `curl -skL http://localhost:5268/api/config` returns the App ID in `logtoAppId`.
- `curl -skL -o /dev/null -w "%{http_code}" http://localhost:5268/api/me` returns `401`: the endpoint is protected.

You can't sign in for the user. Ask them to open `http://localhost:5173`, click **Sign in** with the user from step 4, then **Call /api/me** on the Protected endpoint card, and to add a to-do item. If sign-in fails with a redirect error, the redirect URI in step 4.2 is wrong.

## 7. Wrap up

Point the user to the dashboard (Traces, Metrics) and to "Try failures" in the README. Ask whether to leave the app running; if not, run `aspire stop`.

## 8. Azure (only if the user asks)

This creates billable Azure resources. Before running anything:

- `az account show` and `gh auth status` must both succeed; otherwise ask the user to run `az login` or `gh auth login`.
- Show the subscription from `az account show` and get the user's confirmation that it's the right one.

Then:

```
./scripts/setup-azure-oidc.ps1 -GitHubRepo <owner>/<repo>
gh workflow run Deploy
```

After the first deploy, the Logto setup has to be done again for Azure, with the Azure URLs: see "Set up sign-in" in the README.
