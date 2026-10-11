# Set up a clean Azure deployment

What to do after the first deploy, or after a Deprovision, to get a working app in Azure. A clean deployment has an empty Logto: until these steps are done, the app runs without sign-in, so pages load but nobody can start a business or book.

Values in the `production` environment from an earlier deployment don't work any more: they belong to Logto applications that no longer exist. Steps 8, 9 and 11 replace them.

## 1. Wait for the deploy, then get the addresses

Wait for the Deploy run to finish (**Actions** → **Deploy**). Its log ends with the URL of the Aspire dashboard in Azure. Then list the Container Apps:

```
az containerapp list -g rg-aspire-showcase --query "[].{name:name, fqdn:properties.configuration.ingress.fqdn}" -o table
```

Three of them are needed below: `bff`, the app's address (`<bff-fqdn>`), `logto-admin`, Logto's admin console (`<logto-admin-fqdn>`), and `mcp`, the MCP server (`<mcp-fqdn>`).

## 2. Create the Logto admin account, straight away

Open `https://<logto-admin-fqdn>/console` and create the admin account. Whoever opens it first becomes the admin, and the address is public.

## 3. API resource

**API resources** → **Create API resource**: any name, identifier `https://api.aspire-showcase`. It must match `ApiResource` in `src/AspireShowcase.AppHost/Logto/LogtoExtensions.cs`. On its **Permissions** tab, add `manage:business`.

## 4. Owner role

**Roles** → **Create role**: name `owner`, type **User**, with the `manage:business` permission of that API resource. The API gives it to everyone who starts a business.

## 5. Application for `bff`

**Applications** → **Create application** → **Traditional web**. On it, add:

- **Redirect URIs**: `https://<bff-fqdn>/signin-oidc`
- **Post sign-out redirect URIs**: `https://<bff-fqdn>/signout-callback-oidc`

Note its **App ID** and **App secret**.

## 6. Application for the API

**Applications** → **Create application** → **Machine-to-machine**, for the API to call Logto's Management API. Give it a role with the Logto Management API's `all` permission. Note its **App ID** and **App secret**.

## 7. Allow sign-up

**Sign-in experience**: allow sign-up (e.g. with a username and password), so people can create their account when they start a business or book.

## 8. Give the Logto values to GitHub

```
gh variable set LOGTO_APP_ID --env production --body "<app-id from step 5>"
gh secret set LOGTO_APP_SECRET --env production --body "<app-secret from step 5>"
gh variable set LOGTO_M2M_APP_ID --env production --body "<app-id from step 6>"
gh secret set LOGTO_M2M_APP_SECRET --env production --body "<app-secret from step 6>"
```

## 9. The MCP server's applications

Until these exist, the `mcp` Container App doesn't start, and its log names the missing parameters; the rest of the app works without it. In Logto's admin console:

1. **API resources** → **Create API resource**: any name, identifier exactly `https://<mcp-fqdn>/mcp`. MCP clients get their tokens for this address.
2. **Applications** → **Create application** → **Native app**, for the MCP clients: the only application the server accepts tokens from. Add the **Redirect URIs**:
   - `http://localhost:33418/callback`, for Claude Code. The sign-in comes back to the user's own machine, so it's the same as locally.
   - `https://claude.ai/api/mcp/auth_callback`, to add the server as a custom connector in claude.ai or the Claude desktop app.
3. **Applications** → **Create application** → **Machine-to-machine**, for the MCP server itself, with no roles. In its settings, turn on **token exchange**: the server exchanges the user's token for one for `https://api.aspire-showcase`.

Then give GitHub the Native app's **App ID** and the machine-to-machine app's **App ID** and **App secret**:

```
gh variable set LOGTO_MCP_CLIENT_ID --env production --body "<app-id of the Native app>"
gh variable set LOGTO_MCP_APP_ID --env production --body "<app-id of the machine-to-machine app>"
gh secret set LOGTO_MCP_APP_SECRET --env production --body "<app-secret of the machine-to-machine app>"
```

## 10. Emails (optional)

Without these, everything works and booking emails are skipped and logged.

```
gh secret set RESEND_API_KEY --env production --body "<resend-api-key>"
gh variable set EMAIL_SENDER --env production --body "Bookings <bookings@yourdomain.com>"
```

The sender's domain must be verified in Resend. Resend's test sender, `onboarding@resend.dev`, only delivers to the address of your Resend account.

## 11. Deploy again

The apps read these values when they're deployed:

```
gh workflow run Deploy
```

## 12. Check it works

- `https://<bff-fqdn>/bff/user` shows `"signInEnabled": true`.
- Open `https://<bff-fqdn>` and click **Start your business**: Logto's sign-up opens, then the form. Once the business is created you're signed in again, and the header shows **Owner**.
- Add opening hours, a service and staff. In another browser, open the booking page, `/book/<booking-link>`, sign up as a client and book.
- After a few minutes, Application Insights → **Workbooks** shows the booking on the **Bookings** tab of the API workbook, and the sign-ins on the **Sign-in** tab of the overview workbook.
- `curl -s -o /dev/null -w "%{http_code}" -X POST https://<mcp-fqdn>/mcp` returns `401`, and `https://<mcp-fqdn>/.well-known/oauth-protected-resource/mcp` names the resource `https://<mcp-fqdn>/mcp` and this Logto. To connect Claude Code: `claude mcp add --transport http --client-id <app-id of the Native app> --callback-port 33418 aspire-showcase-prod https://<mcp-fqdn>/mcp`, restart it, then `/mcp` → **aspire-showcase-prod** → **Authenticate**, and ask `Who am I signed in as?`. [Use the MCP server](mcp.md) has examples for every tool.
