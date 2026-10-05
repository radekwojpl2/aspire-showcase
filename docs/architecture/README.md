# Booking SaaS: proposed architecture

A proposal for review: turning the showcase into an appointment booking SaaS for small businesses. Owners sign in, set up services and opening hours, and share a public booking page. Anyone can see the free slots; clients sign in (or create an account) to book one, and can then see and cancel their bookings. A role on each Logto user, owner or client, decides what `web` lets them do.

The React app talks only to a backend for frontend (`bff`), so no token ever reaches the browser: `bff` signs owners and clients in with Logto, keeps their tokens server-side, gives the browser an HttpOnly session cookie, and forwards `/api` calls to `web` with the user's access token. `web` is no longer public.

The model is in [`workspace.dsl`](workspace.dsl) ([Structurizr DSL](https://docs.structurizr.com/dsl)), with five views:

- **Context**: owners, clients and the email service.
- **Containers**: the same resources as the AppHost (`frontend`, `bff`, `web`, `notifications`, `app-db`, `cache`, `logto`).
- **BookASlot**: a client browsing free slots, signing in and booking one, up to the confirmation email.
- **OwnerSignIn**: an owner signing in, and the first API call with the session cookie.
- **AzureDeployment**: what `aspire deploy` would create.

The [arc42](https://arc42.org) documentation is in [`arc42/`](arc42), one file per section, with the diagrams embedded; only sections 3, 5, 6 and 7 have content so far. The viewer shows it under Documentation.

## View it

With Docker running, from the repository root:

```
docker run -it --rm -p 8080:8080 -v "$PWD/docs/architecture:/usr/local/structurizr" structurizr/structurizr local
```

Then open http://localhost:8080.

## Open questions

- Email provider: Azure Communication Services keeps everything in Azure; a service like SendGrid or Resend is quicker to set up.
- Time zones: store bookings in UTC, and opening hours in the business's time zone.
- Sessions live in Redis, which keeps nothing on disk: a restart of `cache` signs everyone out. Fine for a showcase; otherwise give Redis persistence or keep sessions in PostgreSQL.
- How does someone become an owner? Clients can sign up on their own; owners could sign up through a separate "Start your business" page that has `web` give them the owner role through Logto's Management API.
- Logto needs a "Traditional web" application (with an app secret) for `bff` instead of today's "Single page app".
