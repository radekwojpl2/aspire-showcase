# Booking SaaS: proposed architecture

A proposal for review: turning the showcase into an appointment booking SaaS for small businesses. Owners sign up through a "Start your business" page, set up services, staff and opening hours, and share a public booking page. Anyone can see the free slots; clients sign in (or create an account) to book one, and can then see and cancel their bookings. A role on each Logto user, owner or client, decides what `web` lets them do.

The React app talks only to a backend for frontend (`bff`), so no token ever reaches the browser: `bff` signs users in with the self-hosted Logto, keeps their tokens server-side, gives the browser an HttpOnly session cookie, and forwards `/api` calls to `web` with the user's access token. `web` is no longer public.

## Design decisions

- **No overlapping bookings, guaranteed by the database.** Each booking is for a staff member, and an exclusion constraint on (staff, time range) in `app-db` rejects any overlap; `web` answers 409. This also lets a business with several staff take bookings at the same time.
- **Confirmations can't get lost.** `web` saves a booking and its event in an outbox in the same transaction, then relays the event to `notifications` until it's accepted. `notifications` records each event once (by its ID), so a retry never sends a second email.
- **A database per service.** `app-db`, `notifications-db`, `bff-db` and `logto-db` are separate databases on one PostgreSQL server: no service reads another's tables.
- **Sessions survive restarts.** `bff` keeps sessions and its data protection keys in `bff-db`, so a restart or a second replica doesn't sign anyone out. Redis only caches free slots.
- **Tenant isolation is enforced twice.** EF Core query filters and PostgreSQL row-level security on `BusinessId`, so one forgotten filter can't show another business's data.
- **Owners are made, not signed up.** Everyone signs up as a user; creating a business makes `web` give that user the owner role through Logto's Management API.
- **Abuse:** Logto verifies the email on sign-up, and `bff` rate-limits `/api`, per IP for public pages and per user for bookings.

## Views

The model is in [`workspace.dsl`](workspace.dsl) ([Structurizr DSL](https://docs.structurizr.com/dsl)), with six views:

- **Context**: owners, clients and the email service.
- **Containers**: `frontend`, `bff`, `web`, `notifications`, `cache`, `logto` and the four databases.
- **BookASlot**: a client browsing free slots, signing in and booking one, up to the confirmation email.
- **OwnerSignUp**: someone starting a business and getting the owner role.
- **OwnerSignIn**: an owner signing in, and the first API call with the session cookie.
- **AzureDeployment**: what `aspire deploy` would create.

The [arc42](https://arc42.org) documentation is [`arc42.md`](arc42.md), with these diagrams in it as Mermaid, which GitHub renders; only sections 3, 5, 6 and 7 have content so far. After changing the model, update the diagrams there with `scripts/export-diagrams.ps1` (needs Docker).

## View it

With Docker running, from the repository root:

```
docker run -it --rm -p 8080:8080 -v "$PWD/docs/architecture:/usr/local/structurizr" structurizr/structurizr local
```

Then open http://localhost:8080.

## Open questions

- Email provider: Azure Communication Services keeps everything in Azure; a service like SendGrid or Resend is quicker to set up. Logto needs it too, for verification codes.
- Time zones: store bookings in UTC, and opening hours in the business's time zone.
- Logto needs a "Traditional web" application (with an app secret) for `bff` instead of today's "Single page app", and a machine-to-machine application for `web` to call the Management API.
- Self-hosting Logto means its upgrades and uptime are ours: pin the image version, and upgrade it deliberately.
