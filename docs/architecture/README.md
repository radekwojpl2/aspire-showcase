# Booking SaaS: proposed architecture

A proposal for review: turning the showcase into an appointment booking SaaS for small businesses. Owners sign in, set up services and opening hours, and share a public booking page; clients book without an account.

The model is in [`workspace.dsl`](workspace.dsl) ([Structurizr DSL](https://docs.structurizr.com/dsl)), with four views:

- **Context**: owners, clients, Stripe and the email service.
- **Containers**: the same resources as the AppHost (`frontend`, `web`, `notifications`, `app-db`, `cache`, `logto`).
- **BookASlot**: a client booking a slot, from the public page to the confirmation email.
- **AzureDeployment**: what `aspire deploy` would create.

## View it

With Docker running, from the repository root:

```
docker run -it --rm -p 8080:8080 -v "$PWD/docs/architecture:/usr/local/structurizr" structurizr/lite
```

Then open http://localhost:8080.

## Open questions

- Email provider: Azure Communication Services keeps everything in Azure; a service like SendGrid or Resend is quicker to set up.
- Stripe webhooks go to `web`; they could move to their own service if billing grows.
- Time zones: store bookings in UTC, and opening hours in the business's time zone.
