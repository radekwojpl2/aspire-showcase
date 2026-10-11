# Use the MCP server

How to set up sign-in for the MCP server (resource `mcp`), connect Claude Code to it, and try every tool. What the server is and how signing in works is in the README's [MCP server](../README.md#mcp-server) section.

Everything here is local: the app runs with `aspire start`, and the server is at `http://localhost:5068/mcp`.

## 1. Set up Logto

Do this once, after the app's own sign-in works (steps 1 to 6 of the setup skill, `.claude/skills/setup/SKILL.md`). Until it's done, `mcp` doesn't start, and its log (`aspire logs mcp`) says which parameters are missing.

In Logto's admin console, `http://localhost:3002/console`:

1. **API resources** → **Create API resource**: any name, identifier exactly `http://localhost:5068/mcp`. Clients get tokens for this address.
2. **Applications** → **Create application** → **Native app**, for the MCP clients. It's the only application the server accepts tokens from. Under **Redirect URIs**, add `http://localhost:33418/callback`, where Claude Code receives the sign-in, and click **Save changes**. Without it, Logto answers `invalid_redirect_uri`.
3. **Applications** → **Create application** → **Machine-to-machine**, for the MCP server itself, with no roles. In its settings, turn on **token exchange**: the server exchanges the user's token for one for the API.
4. Store the Native app's **App ID**, and the machine-to-machine app's **App ID** and **App secret**, as user secrets of the AppHost:

```
dotnet user-secrets set Parameters:logto-mcp-client-id <native-app-id> --project src/AspireShowcase.AppHost
dotnet user-secrets set Parameters:logto-mcp-app-id <m2m-app-id> --project src/AspireShowcase.AppHost
dotnet user-secrets set Parameters:logto-mcp-app-secret <m2m-app-secret> --project src/AspireShowcase.AppHost
aspire stop
aspire start
aspire wait mcp
```

Check: `curl -s -o /dev/null -w "%{http_code}" -X POST http://localhost:5068/mcp` returns `401`, as nobody is signed in.

## 2. Connect Claude Code

Register the server once, with the Native app's ID. The callback port must match its redirect URI:

```
claude mcp add --transport http --client-id <native-app-id> --callback-port 33418 aspire-showcase http://localhost:5068/mcp
```

It's registered for this folder only; add `--scope user` to have it in every folder. `claude mcp get aspire-showcase` then shows **Needs authentication**.

Then, in Claude Code:

1. Restart Claude Code (`claude --continue` keeps the conversation), so it loads the new server.
2. Run `/mcp`, choose **aspire-showcase**, not **aspire** (that one is Aspire's own MCP server), and **Authenticate**. Logto opens in the browser; sign in.
3. Ask `Who am I signed in as?`

The Code tab of the Claude desktop app uses the same settings: open it in this folder.

**After the app restarts** or gets new tools, run `/mcp` → **aspire-showcase** → **Reconnect**: Claude Code keeps the list of tools it got when it connected.

**To sign in as someone else**, sign out of Logto in the browser first, or it signs you straight back in: open `http://localhost:3001/oidc/session/end`. Then `/mcp` → **aspire-showcase** → **Clear authentication**, and **Authenticate** again.

## 3. Try it

Ask in plain words; Claude picks the tools. Anything that changes a booking or the time off opens a dialog in the terminal first, and nothing happens until you accept it. Businesses are found by their booking link, such as `radekowner`, which `list_businesses` gives.

Two accounts make the whole round: a client, and the owner of a business. Start a business in the app (`http://localhost:5173`, **Start your business**) for the owner. Booking emails are sent for real, so use `@example.com` addresses for made-up clients.

### As a client

| Ask | Tool | Expect |
|---|---|---|
| `Who am I signed in as?` | `whoami` | Your name and email, and no business |
| `Which businesses can I book at?` | `list_businesses` | Each business with its booking link |
| `Find businesses with hair in the name or description` | `list_businesses` | Only the ones that match |
| `Show me <link>'s services, prices and staff` | `get_business` | Services with duration and price, and who does them |
| `What free times does <link> have for a haircut this week?` | `find_free_slots` | This week's free times, day by day |
| `Book me a haircut at <link> on Tuesday` | `book_slot` | A dialog for the time, your name and email; booked once you accept |
| `What are my upcoming bookings?` | `my_bookings` | The booking, and until when it can be changed |
| `Move my <link> booking to another time` | `find_reschedule_slots`, `reschedule_booking` | A dialog to pick the new time |
| `Cancel my booking at <link>` | `cancel_booking` | A dialog to confirm |
| `Show my business calendar` | `owner_calendar` | Refused: "Only the owner of a business can do that" |

### As the owner

| Ask | Tool | Expect |
|---|---|---|
| `Who am I signed in as?` | `whoami` | Your business and its booking link |
| `Show my business calendar for next week` | `owner_calendar` | Every booking, with each client's name and email, and the time off |
| `Book a haircut for Test Client, test.client@example.com, on Tuesday at 15:00` | `owner_book_for_client` | A dialog to confirm the time and the client |
| `Move Test Client's booking on Tuesday to another time` | `owner_reschedule_booking` | A dialog to pick the new time; the client gets an email |
| `Block Monday 08:30 to 10:00 for Owner, note: dentist` | `add_time_off` | A dialog to confirm; the answer lists the bookings already inside it |
| `Cancel Test Client's booking on Monday` | `owner_cancel_booking` | A dialog to confirm; the client gets an email |
| `What time off do I have?` | `list_time_off` | The dentist block |
| `Remove the dentist time off` | `remove_time_off` | A dialog to confirm; the time can be booked again |

Decline a dialog once to see that nothing changes.

Each tool call is a trace in the dashboard (**Traces**) named after the tool, such as `POST /mcp tools/call book_slot`, with the MCP SDK's span for the call (its tool name and session as attributes) and the calls to `web` under it.

## When it doesn't work

| What you see | Why, and what to do |
|---|---|
| `mcp` doesn't start | Its Logto applications aren't set up: step 1. `aspire logs mcp` names the missing parameters. |
| Logto: `invalid_redirect_uri` | The Native app has no redirect URI `http://localhost:33418/callback`: step 1.2. |
| `/mcp` has no **Authenticate** | You chose **aspire**, Aspire's own server; choose **aspire-showcase**. |
| A new tool is missing | Claude Code keeps the old list: `/mcp` → **aspire-showcase** → **Reconnect**. |
| `Not found.` for a business | The booking link is wrong; ask `Which businesses can I book at?` |
| Signing in again picks the same account | Logto remembers you: sign out at `http://localhost:3001/oidc/session/end` first. |
| Chats in the Claude desktop app or claude.ai can't reach it | They connect from Anthropic's servers, which can't reach `localhost`. Only Claude Code (and its desktop Code tab) works locally. |
