---
name: setup
description: Set up the Aspire showcase on this machine - check the prerequisites, start the app, guide the user through the Logto sign-in setup and verify it works. Use when someone wants to run the project for the first time, or when sign-in or starting a business doesn't work.
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
aspire wait bff
aspire wait logto
```

The first start pulls container images and installs npm packages, so it can take a few minutes. Give the user the dashboard link that `aspire start` prints. If a resource doesn't come up, read `aspire describe` and `aspire logs <resource>` and fix that before going on.

## 3. Is sign-in already set up?

```
aspire wait frontend
curl -s http://localhost:5173/bff/user
```

If `signInEnabled` is `true`, skip to step 6. It's only true once the app ID and secret of `bff`'s Logto application are set.

## 4. Logto setup (the user does this in a browser)

Run `aspire wait logto-admin --status up`, then get the `Admin console` URL of the `logto-admin` resource from `aspire describe`. It must be the `http://127.0.0.1:3002/console` one; the console doesn't work on `localhost`.

Give the user these steps, with the values filled in, and wait for the four values:

1. Open the admin console and create the admin account.
2. **API resources** → **Create API resource**: any name, identifier `https://api.aspire-showcase`. On its **Permissions** tab, add `manage:business`.
3. **Roles** → **Create role**: name `owner`, type **User**, with the `manage:business` permission.
4. **Applications** → **Create application** → **Traditional web**. On it, add:
   - **Redirect URIs**: `http://localhost:5173/signin-oidc`
   - **Post sign-out redirect URIs**: `http://localhost:5173/signout-callback-oidc`
5. **Applications** → **Create application** → **Machine-to-machine**, with a role that has the Logto Management API's `all` permission.
6. **Sign-in experience**: allow sign-up, e.g. with a username and password.
7. Paste here the **App ID** and **App secret** of both applications (Traditional web first).

## 5. Apply the values

Set them without echoing the secrets back to the user:

```
dotnet user-secrets set Parameters:logto-app-id <app-id> --project src/AspireShowcase.AppHost
dotnet user-secrets set Parameters:logto-app-secret <app-secret> --project src/AspireShowcase.AppHost
dotnet user-secrets set Parameters:logto-m2m-app-id <m2m-app-id> --project src/AspireShowcase.AppHost
dotnet user-secrets set Parameters:logto-m2m-app-secret <m2m-app-secret> --project src/AspireShowcase.AppHost
aspire stop
aspire start
aspire wait frontend
```

## 6. Verify

- `curl -s http://localhost:5173/bff/user` returns `"signInEnabled":true`.
- `curl -s -o /dev/null -w "%{http_code}" http://localhost:5173/api/businesses/mine -H "X-CSRF: 1"` returns `401`: nobody is signed in.
- `curl -s -o /dev/null -w "%{http_code}" http://localhost:5173/bff/login` returns `302`: the redirect to Logto.

You can't sign in for the user. Ask them to open `http://localhost:5173`, click **Start your business**, create an account, and start a business. The header should then show **Owner**. If Logto shows a redirect URI error, step 4.4 is wrong; if starting the business answers that it couldn't be set up, check `aspire logs web` for the Management API call (step 3 or 5).

## 7. Wrap up

Point the user to the dashboard: Traces shows the business they started as one trace through bff, web and Logto. Ask whether to leave the app running; if not, run `aspire stop`.

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
