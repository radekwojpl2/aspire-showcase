# Booking SaaS: architecture (arc42)

Architecture of the proposed booking SaaS, following the [arc42](https://arc42.org) template. The diagrams come from the Structurizr model in [`workspace.dsl`](workspace.dsl): change the model, then run `scripts/export-diagrams.ps1` to update them here.

1. [Introduction and Goals](#1-introduction-and-goals)
2. [Architecture Constraints](#2-architecture-constraints)
3. [Context and Scope](#3-context-and-scope)
4. [Solution Strategy](#4-solution-strategy)
5. [Building Block View](#5-building-block-view)
6. [Runtime View](#6-runtime-view)
7. [Deployment View](#7-deployment-view)
8. [Cross-cutting Concepts](#8-cross-cutting-concepts)
9. [Architecture Decisions](#9-architecture-decisions)
10. [Quality Requirements](#10-quality-requirements)
11. [Risks and Technical Debt](#11-risks-and-technical-debt)
12. [Glossary](#12-glossary)

## 1. Introduction and Goals

Not written yet.

## 2. Architecture Constraints

Not written yet.

## 3. Context and Scope

<!-- diagram: Context -->
```mermaid
graph LR
  linkStyle default fill:#ffffff

  subgraph diagram ["System Context View: Booking SaaS"]
    style diagram fill:#ffffff,stroke:#ffffff

    1["<div style='font-weight: bold'>Business owner</div><div style='font-size: 70%; margin-top: 0px'>[Person]</div><div style='font-size: 80%; margin-top:10px'>Runs a small business<br />(hairdresser, tutor, physio).<br />Sets up services and hours,<br />manages bookings. The<br />customer of the SaaS.</div>"]
    style 1 fill:#08427b,stroke:#052e56,color:#ffffff
    11("<div style='font-weight: bold'>Email service</div><div style='font-size: 70%; margin-top: 0px'>[Software System]</div><div style='font-size: 80%; margin-top:10px'>Sends transactional email<br />(e.g. Azure Communication<br />Services Email).</div>")
    style 11 fill:#999999,stroke:#6b6b6b,color:#ffffff
    2["<div style='font-weight: bold'>Client</div><div style='font-size: 70%; margin-top: 0px'>[Person]</div><div style='font-size: 80%; margin-top:10px'>Signs in to book an<br />appointment from the<br />business's public page, and<br />sees or cancels their own<br />bookings.</div>"]
    style 2 fill:#08427b,stroke:#052e56,color:#ffffff
    3("<div style='font-weight: bold'>Booking SaaS</div><div style='font-size: 70%; margin-top: 0px'>[Software System]</div><div style='font-size: 80%; margin-top:10px'>Owner dashboard and public<br />booking pages.</div>")
    style 3 fill:#1168bd,stroke:#0b4884,color:#ffffff

    1-. "<div>Manages services, hours and<br />bookings</div><div style='font-size: 70%'>[HTTPS]</div>" .->3
    2-. "<div>Picks a free slot and books</div><div style='font-size: 70%'>[HTTPS]</div>" .->3
    11-. "<div>Confirmation and reminder</div><div style='font-size: 70%'></div>" .->2
    11-. "<div>New booking</div><div style='font-size: 70%'></div>" .->1
    3-. "<div>Sends email</div><div style='font-size: 70%'>[HTTPS]</div>" .->11

  end
```
<!-- /diagram -->

## 4. Solution Strategy

Not written yet.

## 5. Building Block View

### Level 1: Containers

<!-- diagram: Containers -->
```mermaid
graph LR
  linkStyle default fill:#ffffff

  subgraph diagram ["Container View: Booking SaaS"]
    style diagram fill:#ffffff,stroke:#ffffff

    1["<div style='font-weight: bold'>Business owner</div><div style='font-size: 70%; margin-top: 0px'>[Person]</div><div style='font-size: 80%; margin-top:10px'>Runs a small business<br />(hairdresser, tutor, physio).<br />Sets up services and hours,<br />manages bookings. The<br />customer of the SaaS.</div>"]
    style 1 fill:#08427b,stroke:#052e56,color:#ffffff
    2["<div style='font-weight: bold'>Client</div><div style='font-size: 70%; margin-top: 0px'>[Person]</div><div style='font-size: 80%; margin-top:10px'>Signs in to book an<br />appointment from the<br />business's public page, and<br />sees or cancels their own<br />bookings.</div>"]
    style 2 fill:#08427b,stroke:#052e56,color:#ffffff
    11("<div style='font-weight: bold'>Email service</div><div style='font-size: 70%; margin-top: 0px'>[Software System]</div><div style='font-size: 80%; margin-top:10px'>Sends transactional email<br />(e.g. Azure Communication<br />Services Email).</div>")
    style 11 fill:#999999,stroke:#6b6b6b,color:#ffffff

    subgraph 3 ["Booking SaaS"]
      style 3 fill:#ffffff,stroke:#0b4884,color:#0b4884

      10("<div style='font-weight: bold'>logto</div><div style='font-size: 70%; margin-top: 0px'>[Container: Logto]</div><div style='font-size: 80%; margin-top:10px'>Sign-in for owners and<br />clients, with an owner or<br />client role on each user.<br />Clients can create their own<br />account.</div>")
      style 10 fill:#438dd5,stroke:#2e6295,color:#ffffff
      4["<div style='font-weight: bold'>frontend</div><div style='font-size: 70%; margin-top: 0px'>[Container: React + Vite]</div><div style='font-size: 80%; margin-top:10px'>Owner dashboard (calendar,<br />services, hours), the public<br />booking page /book/{slug} and<br />the client's own bookings.</div>"]
      style 4 fill:#438dd5,stroke:#2e6295,color:#ffffff
      5("<div style='font-weight: bold'>bff</div><div style='font-size: 70%; margin-top: 0px'>[Container: ASP.NET Core + YARP]</div><div style='font-size: 80%; margin-top:10px'>Backend for the React app.<br />Signs owners and clients in<br />with Logto (code flow,<br />confidential client), keeps<br />their tokens server-side and<br />gives the browser only an<br />HttpOnly session cookie.<br />Forwards /api/* to web,<br />adding the user's access<br />token, and refreshes it when<br />it expires. Requires an<br />X-CSRF header on /api. In<br />Azure it also serves the<br />built React app.</div>")
      style 5 fill:#438dd5,stroke:#2e6295,color:#ffffff
      6("<div style='font-weight: bold'>web</div><div style='font-size: 70%; margin-top: 0px'>[Container: ASP.NET Core]</div><div style='font-size: 80%; margin-top:10px'>Businesses, services, hours,<br />free slots and bookings.<br />Multi-tenant: every row<br />belongs to a business. The<br />role in the token decides<br />what a user can do: owners<br />manage their business,<br />clients book and see their<br />own bookings. Reachable only<br />from bff; accepts Logto<br />access tokens.</div>")
      style 6 fill:#438dd5,stroke:#2e6295,color:#ffffff
      7("<div style='font-weight: bold'>notifications</div><div style='font-size: 70%; margin-top: 0px'>[Container: ASP.NET Core + Quartz.NET]</div><div style='font-size: 80%; margin-top:10px'>Booking confirmations and<br />cancellations, and a<br />Quartz.NET job that sends<br />reminders 24 h before each<br />appointment.</div>")
      style 7 fill:#438dd5,stroke:#2e6295,color:#ffffff
      8[("<div style='font-weight: bold'>app-db</div><div style='font-size: 70%; margin-top: 0px'>[Container: PostgreSQL]</div><div style='font-size: 80%; margin-top:10px'>Businesses, services, opening<br />hours, bookings. Quartz job<br />store.</div>")]
      style 8 fill:#438dd5,stroke:#2e6295,color:#ffffff
      9[("<div style='font-weight: bold'>cache</div><div style='font-size: 70%; margin-top: 0px'>[Container: Redis]</div><div style='font-size: 80%; margin-top:10px'>Free slots per business and<br />day (invalidated on booking),<br />a short lock per slot so two<br />clients can't book the same<br />one, and bff's sessions and<br />data protection keys.</div>")]
      style 9 fill:#438dd5,stroke:#2e6295,color:#ffffff
    end

    1-. "<div>Manages services, hours and<br />bookings</div><div style='font-size: 70%'>[HTTPS]</div>" .->4
    2-. "<div>Picks a free slot and books</div><div style='font-size: 70%'>[HTTPS]</div>" .->4
    1-. "<div>Signs in on Logto's page</div><div style='font-size: 70%'>[HTTPS]</div>" .->10
    2-. "<div>Signs in or creates an<br />account on Logto's page</div><div style='font-size: 70%'>[HTTPS]</div>" .->10
    11-. "<div>Confirmation and reminder</div><div style='font-size: 70%'></div>" .->2
    11-. "<div>New booking</div><div style='font-size: 70%'></div>" .->1
    4-. "<div>Calls /api with the session<br />cookie</div><div style='font-size: 70%'>[JSON/HTTPS]</div>" .->5
    5-. "<div>Signs users in, refreshes<br />their tokens</div><div style='font-size: 70%'>[OIDC code flow]</div>" .->10
    5-. "<div>Forwards /api, with the<br />user's access token</div><div style='font-size: 70%'>[HTTPS + Bearer]</div>" .->6
    5-. "<div>Keeps sessions and data<br />protection keys</div><div style='font-size: 70%'>[StackExchange.Redis]</div>" .->9
    6-. "<div>Validates access tokens</div><div style='font-size: 70%'>[JWKS]</div>" .->10
    6-. "<div>Reads and writes</div><div style='font-size: 70%'>[EF Core / Npgsql]</div>" .->8
    6-. "<div>Caches free slots, locks a<br />slot while booking</div><div style='font-size: 70%'>[StackExchange.Redis]</div>" .->9
    6-. "<div>Booking created or cancelled</div><div style='font-size: 70%'>[JSON/HTTPS]</div>" .->7
    7-. "<div>Reads upcoming bookings,<br />stores Quartz jobs</div><div style='font-size: 70%'>[Npgsql]</div>" .->8
    7-. "<div>Sends email</div><div style='font-size: 70%'>[HTTPS]</div>" .->11

  end
```
<!-- /diagram -->

## 6. Runtime View

### A client books a slot

<!-- diagram: BookASlot -->
```mermaid
graph LR
  linkStyle default fill:#ffffff

  subgraph diagram ["Dynamic View: Booking SaaS"]
    style diagram fill:#ffffff,stroke:#ffffff

    subgraph 3 ["Booking SaaS"]
      style 3 fill:#ffffff,stroke:#0b4884,color:#0b4884

      10("<div style='font-weight: bold'>logto</div><div style='font-size: 70%; margin-top: 0px'>[Container: Logto]</div><div style='font-size: 80%; margin-top:10px'>Sign-in for owners and<br />clients, with an owner or<br />client role on each user.<br />Clients can create their own<br />account.</div>")
      style 10 fill:#438dd5,stroke:#2e6295,color:#ffffff
      4["<div style='font-weight: bold'>frontend</div><div style='font-size: 70%; margin-top: 0px'>[Container: React + Vite]</div><div style='font-size: 80%; margin-top:10px'>Owner dashboard (calendar,<br />services, hours), the public<br />booking page /book/{slug} and<br />the client's own bookings.</div>"]
      style 4 fill:#438dd5,stroke:#2e6295,color:#ffffff
      5("<div style='font-weight: bold'>bff</div><div style='font-size: 70%; margin-top: 0px'>[Container: ASP.NET Core + YARP]</div><div style='font-size: 80%; margin-top:10px'>Backend for the React app.<br />Signs owners and clients in<br />with Logto (code flow,<br />confidential client), keeps<br />their tokens server-side and<br />gives the browser only an<br />HttpOnly session cookie.<br />Forwards /api/* to web,<br />adding the user's access<br />token, and refreshes it when<br />it expires. Requires an<br />X-CSRF header on /api. In<br />Azure it also serves the<br />built React app.</div>")
      style 5 fill:#438dd5,stroke:#2e6295,color:#ffffff
      6("<div style='font-weight: bold'>web</div><div style='font-size: 70%; margin-top: 0px'>[Container: ASP.NET Core]</div><div style='font-size: 80%; margin-top:10px'>Businesses, services, hours,<br />free slots and bookings.<br />Multi-tenant: every row<br />belongs to a business. The<br />role in the token decides<br />what a user can do: owners<br />manage their business,<br />clients book and see their<br />own bookings. Reachable only<br />from bff; accepts Logto<br />access tokens.</div>")
      style 6 fill:#438dd5,stroke:#2e6295,color:#ffffff
      7("<div style='font-weight: bold'>notifications</div><div style='font-size: 70%; margin-top: 0px'>[Container: ASP.NET Core + Quartz.NET]</div><div style='font-size: 80%; margin-top:10px'>Booking confirmations and<br />cancellations, and a<br />Quartz.NET job that sends<br />reminders 24 h before each<br />appointment.</div>")
      style 7 fill:#438dd5,stroke:#2e6295,color:#ffffff
      8[("<div style='font-weight: bold'>app-db</div><div style='font-size: 70%; margin-top: 0px'>[Container: PostgreSQL]</div><div style='font-size: 80%; margin-top:10px'>Businesses, services, opening<br />hours, bookings. Quartz job<br />store.</div>")]
      style 8 fill:#438dd5,stroke:#2e6295,color:#ffffff
      9[("<div style='font-weight: bold'>cache</div><div style='font-size: 70%; margin-top: 0px'>[Container: Redis]</div><div style='font-size: 80%; margin-top:10px'>Free slots per business and<br />day (invalidated on booking),<br />a short lock per slot so two<br />clients can't book the same<br />one, and bff's sessions and<br />data protection keys.</div>")]
      style 9 fill:#438dd5,stroke:#2e6295,color:#ffffff
    end

    2["<div style='font-weight: bold'>Client</div><div style='font-size: 70%; margin-top: 0px'>[Person]</div><div style='font-size: 80%; margin-top:10px'>Signs in to book an<br />appointment from the<br />business's public page, and<br />sees or cancels their own<br />bookings.</div>"]
    style 2 fill:#08427b,stroke:#052e56,color:#ffffff
    11("<div style='font-weight: bold'>Email service</div><div style='font-size: 70%; margin-top: 0px'>[Software System]</div><div style='font-size: 80%; margin-top:10px'>Sends transactional email<br />(e.g. Azure Communication<br />Services Email).</div>")
    style 11 fill:#999999,stroke:#6b6b6b,color:#ffffff

    2-. "<div>1. Opens /book/{slug} and<br />picks a slot</div><div style='font-size: 70%'>[HTTPS]</div>" .->4
    4-. "<div>2. GET free slots (public, no<br />session needed)</div><div style='font-size: 70%'>[JSON/HTTPS]</div>" .->5
    5-. "<div>3. Forwards, without a token</div><div style='font-size: 70%'>[HTTPS + Bearer]</div>" .->6
    6-. "<div>4. Free slots from the cache<br />(database on a miss)</div><div style='font-size: 70%'>[StackExchange.Redis]</div>" .->9
    4-. "<div>5. Clicks Book while signed<br />out:<br />/bff/login?returnUrl=/book/{slug}</div><div style='font-size: 70%'>[JSON/HTTPS]</div>" .->5
    2-. "<div>6. Signs in, or creates an<br />account</div><div style='font-size: 70%'>[HTTPS]</div>" .->10
    5-. "<div>7. Exchanges the code for<br />tokens</div><div style='font-size: 70%'>[OIDC code flow]</div>" .->10
    5-. "<div>8. Stores the session, sets<br />the session cookie</div><div style='font-size: 70%'>[StackExchange.Redis]</div>" .->9
    4-. "<div>9. POST booking with the<br />cookie and X-CSRF header</div><div style='font-size: 70%'>[JSON/HTTPS]</div>" .->5
    5-. "<div>10. Forwards with the<br />client's access token</div><div style='font-size: 70%'>[HTTPS + Bearer]</div>" .->6
    6-. "<div>11. Locks the slot</div><div style='font-size: 70%'>[StackExchange.Redis]</div>" .->9
    6-. "<div>12. Inserts the booking for<br />this client (unique business<br />+ start time)</div><div style='font-size: 70%'>[EF Core / Npgsql]</div>" .->8
    6-. "<div>13. Booking created</div><div style='font-size: 70%'>[JSON/HTTPS]</div>" .->7
    7-. "<div>14. Sends the confirmation</div><div style='font-size: 70%'>[HTTPS]</div>" .->11
    11-. "<div>15. Confirmation email</div><div style='font-size: 70%'></div>" .->2

  end
```
<!-- /diagram -->

### An owner signs in

<!-- diagram: OwnerSignIn -->
```mermaid
graph LR
  linkStyle default fill:#ffffff

  subgraph diagram ["Dynamic View: Booking SaaS"]
    style diagram fill:#ffffff,stroke:#ffffff

    subgraph 3 ["Booking SaaS"]
      style 3 fill:#ffffff,stroke:#0b4884,color:#0b4884

      10("<div style='font-weight: bold'>logto</div><div style='font-size: 70%; margin-top: 0px'>[Container: Logto]</div><div style='font-size: 80%; margin-top:10px'>Sign-in for owners and<br />clients, with an owner or<br />client role on each user.<br />Clients can create their own<br />account.</div>")
      style 10 fill:#438dd5,stroke:#2e6295,color:#ffffff
      4["<div style='font-weight: bold'>frontend</div><div style='font-size: 70%; margin-top: 0px'>[Container: React + Vite]</div><div style='font-size: 80%; margin-top:10px'>Owner dashboard (calendar,<br />services, hours), the public<br />booking page /book/{slug} and<br />the client's own bookings.</div>"]
      style 4 fill:#438dd5,stroke:#2e6295,color:#ffffff
      5("<div style='font-weight: bold'>bff</div><div style='font-size: 70%; margin-top: 0px'>[Container: ASP.NET Core + YARP]</div><div style='font-size: 80%; margin-top:10px'>Backend for the React app.<br />Signs owners and clients in<br />with Logto (code flow,<br />confidential client), keeps<br />their tokens server-side and<br />gives the browser only an<br />HttpOnly session cookie.<br />Forwards /api/* to web,<br />adding the user's access<br />token, and refreshes it when<br />it expires. Requires an<br />X-CSRF header on /api. In<br />Azure it also serves the<br />built React app.</div>")
      style 5 fill:#438dd5,stroke:#2e6295,color:#ffffff
      6("<div style='font-weight: bold'>web</div><div style='font-size: 70%; margin-top: 0px'>[Container: ASP.NET Core]</div><div style='font-size: 80%; margin-top:10px'>Businesses, services, hours,<br />free slots and bookings.<br />Multi-tenant: every row<br />belongs to a business. The<br />role in the token decides<br />what a user can do: owners<br />manage their business,<br />clients book and see their<br />own bookings. Reachable only<br />from bff; accepts Logto<br />access tokens.</div>")
      style 6 fill:#438dd5,stroke:#2e6295,color:#ffffff
      9[("<div style='font-weight: bold'>cache</div><div style='font-size: 70%; margin-top: 0px'>[Container: Redis]</div><div style='font-size: 80%; margin-top:10px'>Free slots per business and<br />day (invalidated on booking),<br />a short lock per slot so two<br />clients can't book the same<br />one, and bff's sessions and<br />data protection keys.</div>")]
      style 9 fill:#438dd5,stroke:#2e6295,color:#ffffff
    end

    1["<div style='font-weight: bold'>Business owner</div><div style='font-size: 70%; margin-top: 0px'>[Person]</div><div style='font-size: 80%; margin-top:10px'>Runs a small business<br />(hairdresser, tutor, physio).<br />Sets up services and hours,<br />manages bookings. The<br />customer of the SaaS.</div>"]
    style 1 fill:#08427b,stroke:#052e56,color:#ffffff

    1-. "<div>1. Clicks Sign in</div><div style='font-size: 70%'>[HTTPS]</div>" .->4
    4-. "<div>2. Goes to /bff/login</div><div style='font-size: 70%'>[JSON/HTTPS]</div>" .->5
    1-. "<div>3. Redirected to Logto, signs<br />in</div><div style='font-size: 70%'>[HTTPS]</div>" .->10
    5-. "<div>4. Gets the code back on<br />/signin-oidc, exchanges it<br />for ID, access and refresh<br />tokens</div><div style='font-size: 70%'>[OIDC code flow]</div>" .->10
    5-. "<div>5. Stores the session with<br />the tokens, sets the session<br />cookie</div><div style='font-size: 70%'>[StackExchange.Redis]</div>" .->9
    4-. "<div>6. GET /api/bookings with the<br />cookie and X-CSRF header</div><div style='font-size: 70%'>[JSON/HTTPS]</div>" .->5
    5-. "<div>7. Forwards with<br />Authorization: Bearer <access<br />token></div><div style='font-size: 70%'>[HTTPS + Bearer]</div>" .->6
    6-. "<div>8. Checks the token signature<br />(JWKS, cached)</div><div style='font-size: 70%'>[JWKS]</div>" .->10

  end
```
<!-- /diagram -->

## 7. Deployment View

### Azure

<!-- diagram: AzureDeployment -->
```mermaid
graph LR
  linkStyle default fill:#ffffff

  subgraph diagram ["Deployment View: Booking SaaS - Azure"]
    style diagram fill:#ffffff,stroke:#ffffff

    subgraph 31 ["Azure"]
      style 31 fill:#ffffff,stroke:#444444,color:#444444

      subgraph 32 ["Container Apps environment"]
        style 32 fill:#ffffff,stroke:#444444,color:#444444

        subgraph 33 ["bff"]
          style 33 fill:#ffffff,stroke:#444444,color:#444444

          34("<div style='font-weight: bold'>bff</div><div style='font-size: 70%; margin-top: 0px'>[Container: ASP.NET Core + YARP]</div><div style='font-size: 80%; margin-top:10px'>Backend for the React app.<br />Signs owners and clients in<br />with Logto (code flow,<br />confidential client), keeps<br />their tokens server-side and<br />gives the browser only an<br />HttpOnly session cookie.<br />Forwards /api/* to web,<br />adding the user's access<br />token, and refreshes it when<br />it expires. Requires an<br />X-CSRF header on /api. In<br />Azure it also serves the<br />built React app.</div>")
          style 34 fill:#438dd5,stroke:#2e6295,color:#ffffff
          35["<div style='font-weight: bold'>frontend</div><div style='font-size: 70%; margin-top: 0px'>[Container: React + Vite]</div><div style='font-size: 80%; margin-top:10px'>Owner dashboard (calendar,<br />services, hours), the public<br />booking page /book/{slug} and<br />the client's own bookings.</div>"]
          style 35 fill:#438dd5,stroke:#2e6295,color:#ffffff
        end

        subgraph 37 ["web"]
          style 37 fill:#ffffff,stroke:#444444,color:#444444

          38("<div style='font-weight: bold'>web</div><div style='font-size: 70%; margin-top: 0px'>[Container: ASP.NET Core]</div><div style='font-size: 80%; margin-top:10px'>Businesses, services, hours,<br />free slots and bookings.<br />Multi-tenant: every row<br />belongs to a business. The<br />role in the token decides<br />what a user can do: owners<br />manage their business,<br />clients book and see their<br />own bookings. Reachable only<br />from bff; accepts Logto<br />access tokens.</div>")
          style 38 fill:#438dd5,stroke:#2e6295,color:#ffffff
        end

        subgraph 40 ["notifications"]
          style 40 fill:#ffffff,stroke:#444444,color:#444444

          41("<div style='font-weight: bold'>notifications</div><div style='font-size: 70%; margin-top: 0px'>[Container: ASP.NET Core + Quartz.NET]</div><div style='font-size: 80%; margin-top:10px'>Booking confirmations and<br />cancellations, and a<br />Quartz.NET job that sends<br />reminders 24 h before each<br />appointment.</div>")
          style 41 fill:#438dd5,stroke:#2e6295,color:#ffffff
        end

        subgraph 43 ["cache"]
          style 43 fill:#ffffff,stroke:#444444,color:#444444

          44[("<div style='font-weight: bold'>cache</div><div style='font-size: 70%; margin-top: 0px'>[Container: Redis]</div><div style='font-size: 80%; margin-top:10px'>Free slots per business and<br />day (invalidated on booking),<br />a short lock per slot so two<br />clients can't book the same<br />one, and bff's sessions and<br />data protection keys.</div>")]
          style 44 fill:#438dd5,stroke:#2e6295,color:#ffffff
        end

        subgraph 47 ["logto"]
          style 47 fill:#ffffff,stroke:#444444,color:#444444

          48("<div style='font-weight: bold'>logto</div><div style='font-size: 70%; margin-top: 0px'>[Container: Logto]</div><div style='font-size: 80%; margin-top:10px'>Sign-in for owners and<br />clients, with an owner or<br />client role on each user.<br />Clients can create their own<br />account.</div>")
          style 48 fill:#438dd5,stroke:#2e6295,color:#ffffff
        end

      end

      subgraph 51 ["PostgreSQL"]
        style 51 fill:#ffffff,stroke:#444444,color:#444444

        52[("<div style='font-weight: bold'>app-db</div><div style='font-size: 70%; margin-top: 0px'>[Container: PostgreSQL]</div><div style='font-size: 80%; margin-top:10px'>Businesses, services, opening<br />hours, bookings. Quartz job<br />store.</div>")]
        style 52 fill:#438dd5,stroke:#2e6295,color:#ffffff
      end

      55("<div style='font-weight: bold'>Key Vault</div><div style='font-size: 70%; margin-top: 0px'>[Infrastructure Node: Azure Key Vault]</div><div style='font-size: 80%; margin-top:10px'>Connection strings and<br />secrets (email), read with<br />managed identities.</div>")
      style 55 fill:#ffffff,stroke:#b2b2b2,color:#000000
      56("<div style='font-weight: bold'>Application Insights</div><div style='font-size: 70%; margin-top: 0px'>[Infrastructure Node: Azure Monitor]</div><div style='font-size: 80%; margin-top:10px'>Logs, traces and metrics from<br />the services.</div>")
      style 56 fill:#ffffff,stroke:#b2b2b2,color:#000000
    end

    35-. "<div>Calls /api with the session<br />cookie</div><div style='font-size: 70%'>[JSON/HTTPS]</div>" .->34
    34-. "<div>Forwards /api, with the<br />user's access token</div><div style='font-size: 70%'>[HTTPS + Bearer]</div>" .->38
    38-. "<div>Booking created or cancelled</div><div style='font-size: 70%'>[JSON/HTTPS]</div>" .->41
    34-. "<div>Keeps sessions and data<br />protection keys</div><div style='font-size: 70%'>[StackExchange.Redis]</div>" .->44
    38-. "<div>Caches free slots, locks a<br />slot while booking</div><div style='font-size: 70%'>[StackExchange.Redis]</div>" .->44
    34-. "<div>Signs users in, refreshes<br />their tokens</div><div style='font-size: 70%'>[OIDC code flow]</div>" .->48
    38-. "<div>Validates access tokens</div><div style='font-size: 70%'>[JWKS]</div>" .->48
    38-. "<div>Reads and writes</div><div style='font-size: 70%'>[EF Core / Npgsql]</div>" .->52
    41-. "<div>Reads upcoming bookings,<br />stores Quartz jobs</div><div style='font-size: 70%'>[Npgsql]</div>" .->52

  end
```
<!-- /diagram -->

## 8. Cross-cutting Concepts

Not written yet.

## 9. Architecture Decisions

Not written yet.

## 10. Quality Requirements

Not written yet.

## 11. Risks and Technical Debt

Not written yet.

## 12. Glossary

Not written yet.
