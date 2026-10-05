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

    1["<div style='font-weight: bold'>Business owner</div><div style='font-size: 70%; margin-top: 0px'>[Person]</div><div style='font-size: 80%; margin-top:10px'>Runs a small business<br />(hairdresser, tutor, physio).<br />Sets up services, staff and<br />hours, manages bookings. The<br />customer of the SaaS.</div>"]
    style 1 fill:#08427b,stroke:#052e56,color:#ffffff
    14("<div style='font-weight: bold'>Email service</div><div style='font-size: 70%; margin-top: 0px'>[Software System]</div><div style='font-size: 80%; margin-top:10px'>Sends transactional email<br />(e.g. Azure Communication<br />Services Email).</div>")
    style 14 fill:#999999,stroke:#6b6b6b,color:#ffffff
    2["<div style='font-weight: bold'>Client</div><div style='font-size: 70%; margin-top: 0px'>[Person]</div><div style='font-size: 80%; margin-top:10px'>Signs in to book an<br />appointment from the<br />business's public page, and<br />sees or cancels their own<br />bookings.</div>"]
    style 2 fill:#08427b,stroke:#052e56,color:#ffffff
    3("<div style='font-weight: bold'>Booking SaaS</div><div style='font-size: 70%; margin-top: 0px'>[Software System]</div><div style='font-size: 80%; margin-top:10px'>Owner dashboard and public<br />booking pages.</div>")
    style 3 fill:#1168bd,stroke:#0b4884,color:#ffffff

    1-. "<div>Sets up their business,<br />manages bookings</div><div style='font-size: 70%'>[HTTPS]</div>" .->3
    2-. "<div>Picks a free slot and books</div><div style='font-size: 70%'>[HTTPS]</div>" .->3
    14-. "<div>Confirmation and reminder</div><div style='font-size: 70%'></div>" .->2
    14-. "<div>New booking</div><div style='font-size: 70%'></div>" .->1
    3-. "<div>Sends email</div><div style='font-size: 70%'>[HTTPS]</div>" .->14

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

    1["<div style='font-weight: bold'>Business owner</div><div style='font-size: 70%; margin-top: 0px'>[Person]</div><div style='font-size: 80%; margin-top:10px'>Runs a small business<br />(hairdresser, tutor, physio).<br />Sets up services, staff and<br />hours, manages bookings. The<br />customer of the SaaS.</div>"]
    style 1 fill:#08427b,stroke:#052e56,color:#ffffff
    2["<div style='font-weight: bold'>Client</div><div style='font-size: 70%; margin-top: 0px'>[Person]</div><div style='font-size: 80%; margin-top:10px'>Signs in to book an<br />appointment from the<br />business's public page, and<br />sees or cancels their own<br />bookings.</div>"]
    style 2 fill:#08427b,stroke:#052e56,color:#ffffff
    14("<div style='font-weight: bold'>Email service</div><div style='font-size: 70%; margin-top: 0px'>[Software System]</div><div style='font-size: 80%; margin-top:10px'>Sends transactional email<br />(e.g. Azure Communication<br />Services Email).</div>")
    style 14 fill:#999999,stroke:#6b6b6b,color:#ffffff

    subgraph 3 ["Booking SaaS"]
      style 3 fill:#ffffff,stroke:#0b4884,color:#0b4884

      10[("<div style='font-weight: bold'>bff-db</div><div style='font-size: 70%; margin-top: 0px'>[Container: PostgreSQL]</div><div style='font-size: 80%; margin-top:10px'>Sessions with the users'<br />tokens, and the data<br />protection keys that encrypt<br />the session cookie. Survives<br />restarts and is shared by<br />every bff replica.</div>")]
      style 10 fill:#438dd5,stroke:#2e6295,color:#ffffff
      11[("<div style='font-weight: bold'>cache</div><div style='font-size: 70%; margin-top: 0px'>[Container: Redis]</div><div style='font-size: 80%; margin-top:10px'>Free slots per business and<br />day, invalidated when a<br />booking is made or cancelled.<br />Only a cache: losing it costs<br />a database query, nothing<br />else.</div>")]
      style 11 fill:#438dd5,stroke:#2e6295,color:#ffffff
      12("<div style='font-weight: bold'>logto</div><div style='font-size: 70%; margin-top: 0px'>[Container: Logto]</div><div style='font-size: 80%; margin-top:10px'>Self-hosted sign-in and<br />sign-up for owners and<br />clients, with an owner or<br />client role on each user.<br />Email verification on sign-up<br />through an email connector.<br />The admin console runs as a<br />second instance<br />(logto-admin).</div>")
      style 12 fill:#438dd5,stroke:#2e6295,color:#ffffff
      13[("<div style='font-weight: bold'>logto-db</div><div style='font-size: 70%; margin-top: 0px'>[Container: PostgreSQL]</div><div style='font-size: 80%; margin-top:10px'>Logto's users, roles and<br />applications. Backed up with<br />the PostgreSQL server.</div>")]
      style 13 fill:#438dd5,stroke:#2e6295,color:#ffffff
      4["<div style='font-weight: bold'>frontend</div><div style='font-size: 70%; margin-top: 0px'>[Container: React + Vite]</div><div style='font-size: 80%; margin-top:10px'>Owner dashboard (calendar,<br />services, staff, hours), the<br />'Start your business'<br />sign-up, the public booking<br />page /book/{slug} and the<br />client's own bookings.</div>"]
      style 4 fill:#438dd5,stroke:#2e6295,color:#ffffff
      5("<div style='font-weight: bold'>bff</div><div style='font-size: 70%; margin-top: 0px'>[Container: ASP.NET Core + YARP]</div><div style='font-size: 80%; margin-top:10px'>Backend for the React app.<br />Signs users in with Logto<br />(code flow, confidential<br />client), keeps their tokens<br />server-side and gives the<br />browser only an HttpOnly<br />session cookie. Forwards<br />/api/* to web with the user's<br />access token, refreshing it<br />when it expires. Requires an<br />X-CSRF header on /api, and<br />rate-limits it: per IP for<br />public pages, per user for<br />bookings. In Azure it also<br />serves the built React app.</div>")
      style 5 fill:#438dd5,stroke:#2e6295,color:#ffffff
      6("<div style='font-weight: bold'>web</div><div style='font-size: 70%; margin-top: 0px'>[Container: ASP.NET Core]</div><div style='font-size: 80%; margin-top:10px'>Businesses, services, staff,<br />hours, free slots and<br />bookings. Owns app-db. Tenant<br />isolation: every row has a<br />BusinessId, enforced by EF<br />Core query filters and<br />PostgreSQL row-level<br />security. The role in the<br />token decides what a user can<br />do. Writes events to an<br />outbox and relays them to<br />notifications. Reachable only<br />from bff.</div>")
      style 6 fill:#438dd5,stroke:#2e6295,color:#ffffff
      7("<div style='font-weight: bold'>notifications</div><div style='font-size: 70%; margin-top: 0px'>[Container: ASP.NET Core + Quartz.NET]</div><div style='font-size: 80%; margin-top:10px'>Owns notifications-db.<br />Receives booking events<br />(idempotent by event ID),<br />sends confirmations and<br />cancellations, and schedules<br />reminders 24 h before each<br />appointment with Quartz.NET.</div>")
      style 7 fill:#438dd5,stroke:#2e6295,color:#ffffff
      8[("<div style='font-weight: bold'>app-db</div><div style='font-size: 70%; margin-top: 0px'>[Container: PostgreSQL]</div><div style='font-size: 80%; margin-top:10px'>Businesses, services, staff,<br />opening hours, bookings and<br />the outbox. An exclusion<br />constraint on (staff, time<br />range) makes overlapping<br />bookings impossible.</div>")]
      style 8 fill:#438dd5,stroke:#2e6295,color:#ffffff
      9[("<div style='font-weight: bold'>notifications-db</div><div style='font-size: 70%; margin-top: 0px'>[Container: PostgreSQL]</div><div style='font-size: 80%; margin-top:10px'>What notifications needs to<br />remind people (appointment<br />time, recipient), the emails<br />sent, and the Quartz job<br />store.</div>")]
      style 9 fill:#438dd5,stroke:#2e6295,color:#ffffff
    end

    1-. "<div>Sets up their business,<br />manages bookings</div><div style='font-size: 70%'>[HTTPS]</div>" .->4
    2-. "<div>Picks a free slot and books</div><div style='font-size: 70%'>[HTTPS]</div>" .->4
    1-. "<div>Signs up and signs in</div><div style='font-size: 70%'>[HTTPS]</div>" .->12
    2-. "<div>Signs up and signs in</div><div style='font-size: 70%'>[HTTPS]</div>" .->12
    14-. "<div>Confirmation and reminder</div><div style='font-size: 70%'></div>" .->2
    14-. "<div>New booking</div><div style='font-size: 70%'></div>" .->1
    4-. "<div>Calls /api with the session<br />cookie</div><div style='font-size: 70%'>[JSON/HTTPS]</div>" .->5
    5-. "<div>Signs users in, refreshes<br />their tokens</div><div style='font-size: 70%'>[OIDC code flow]</div>" .->12
    5-. "<div>Forwards /api, with the<br />user's access token</div><div style='font-size: 70%'>[HTTPS + Bearer]</div>" .->6
    5-. "<div>Keeps sessions and data<br />protection keys</div><div style='font-size: 70%'>[EF Core / Npgsql]</div>" .->10
    6-. "<div>Validates access tokens<br />(JWKS), assigns the owner<br />role (Management API)</div><div style='font-size: 70%'>[HTTPS]</div>" .->12
    6-. "<div>Reads and writes; a booking<br />and its outbox event in one<br />transaction</div><div style='font-size: 70%'>[EF Core / Npgsql]</div>" .->8
    6-. "<div>Caches free slots</div><div style='font-size: 70%'>[StackExchange.Redis]</div>" .->11
    6-. "<div>Relays outbox events, retried<br />until accepted</div><div style='font-size: 70%'>[JSON/HTTPS]</div>" .->7
    7-. "<div>Reads and writes</div><div style='font-size: 70%'>[EF Core / Npgsql]</div>" .->9
    7-. "<div>Sends email</div><div style='font-size: 70%'>[HTTPS]</div>" .->14
    12-. "<div>Reads and writes</div><div style='font-size: 70%'>[PostgreSQL]</div>" .->13
    12-. "<div>Sends verification codes</div><div style='font-size: 70%'>[HTTPS]</div>" .->14

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

      10[("<div style='font-weight: bold'>bff-db</div><div style='font-size: 70%; margin-top: 0px'>[Container: PostgreSQL]</div><div style='font-size: 80%; margin-top:10px'>Sessions with the users'<br />tokens, and the data<br />protection keys that encrypt<br />the session cookie. Survives<br />restarts and is shared by<br />every bff replica.</div>")]
      style 10 fill:#438dd5,stroke:#2e6295,color:#ffffff
      11[("<div style='font-weight: bold'>cache</div><div style='font-size: 70%; margin-top: 0px'>[Container: Redis]</div><div style='font-size: 80%; margin-top:10px'>Free slots per business and<br />day, invalidated when a<br />booking is made or cancelled.<br />Only a cache: losing it costs<br />a database query, nothing<br />else.</div>")]
      style 11 fill:#438dd5,stroke:#2e6295,color:#ffffff
      12("<div style='font-weight: bold'>logto</div><div style='font-size: 70%; margin-top: 0px'>[Container: Logto]</div><div style='font-size: 80%; margin-top:10px'>Self-hosted sign-in and<br />sign-up for owners and<br />clients, with an owner or<br />client role on each user.<br />Email verification on sign-up<br />through an email connector.<br />The admin console runs as a<br />second instance<br />(logto-admin).</div>")
      style 12 fill:#438dd5,stroke:#2e6295,color:#ffffff
      4["<div style='font-weight: bold'>frontend</div><div style='font-size: 70%; margin-top: 0px'>[Container: React + Vite]</div><div style='font-size: 80%; margin-top:10px'>Owner dashboard (calendar,<br />services, staff, hours), the<br />'Start your business'<br />sign-up, the public booking<br />page /book/{slug} and the<br />client's own bookings.</div>"]
      style 4 fill:#438dd5,stroke:#2e6295,color:#ffffff
      5("<div style='font-weight: bold'>bff</div><div style='font-size: 70%; margin-top: 0px'>[Container: ASP.NET Core + YARP]</div><div style='font-size: 80%; margin-top:10px'>Backend for the React app.<br />Signs users in with Logto<br />(code flow, confidential<br />client), keeps their tokens<br />server-side and gives the<br />browser only an HttpOnly<br />session cookie. Forwards<br />/api/* to web with the user's<br />access token, refreshing it<br />when it expires. Requires an<br />X-CSRF header on /api, and<br />rate-limits it: per IP for<br />public pages, per user for<br />bookings. In Azure it also<br />serves the built React app.</div>")
      style 5 fill:#438dd5,stroke:#2e6295,color:#ffffff
      6("<div style='font-weight: bold'>web</div><div style='font-size: 70%; margin-top: 0px'>[Container: ASP.NET Core]</div><div style='font-size: 80%; margin-top:10px'>Businesses, services, staff,<br />hours, free slots and<br />bookings. Owns app-db. Tenant<br />isolation: every row has a<br />BusinessId, enforced by EF<br />Core query filters and<br />PostgreSQL row-level<br />security. The role in the<br />token decides what a user can<br />do. Writes events to an<br />outbox and relays them to<br />notifications. Reachable only<br />from bff.</div>")
      style 6 fill:#438dd5,stroke:#2e6295,color:#ffffff
      7("<div style='font-weight: bold'>notifications</div><div style='font-size: 70%; margin-top: 0px'>[Container: ASP.NET Core + Quartz.NET]</div><div style='font-size: 80%; margin-top:10px'>Owns notifications-db.<br />Receives booking events<br />(idempotent by event ID),<br />sends confirmations and<br />cancellations, and schedules<br />reminders 24 h before each<br />appointment with Quartz.NET.</div>")
      style 7 fill:#438dd5,stroke:#2e6295,color:#ffffff
      8[("<div style='font-weight: bold'>app-db</div><div style='font-size: 70%; margin-top: 0px'>[Container: PostgreSQL]</div><div style='font-size: 80%; margin-top:10px'>Businesses, services, staff,<br />opening hours, bookings and<br />the outbox. An exclusion<br />constraint on (staff, time<br />range) makes overlapping<br />bookings impossible.</div>")]
      style 8 fill:#438dd5,stroke:#2e6295,color:#ffffff
      9[("<div style='font-weight: bold'>notifications-db</div><div style='font-size: 70%; margin-top: 0px'>[Container: PostgreSQL]</div><div style='font-size: 80%; margin-top:10px'>What notifications needs to<br />remind people (appointment<br />time, recipient), the emails<br />sent, and the Quartz job<br />store.</div>")]
      style 9 fill:#438dd5,stroke:#2e6295,color:#ffffff
    end

    2["<div style='font-weight: bold'>Client</div><div style='font-size: 70%; margin-top: 0px'>[Person]</div><div style='font-size: 80%; margin-top:10px'>Signs in to book an<br />appointment from the<br />business's public page, and<br />sees or cancels their own<br />bookings.</div>"]
    style 2 fill:#08427b,stroke:#052e56,color:#ffffff
    14("<div style='font-weight: bold'>Email service</div><div style='font-size: 70%; margin-top: 0px'>[Software System]</div><div style='font-size: 80%; margin-top:10px'>Sends transactional email<br />(e.g. Azure Communication<br />Services Email).</div>")
    style 14 fill:#999999,stroke:#6b6b6b,color:#ffffff

    2-. "<div>1. Opens /book/{slug} and<br />picks a slot</div><div style='font-size: 70%'>[HTTPS]</div>" .->4
    4-. "<div>2. GET free slots (public,<br />rate-limited per IP)</div><div style='font-size: 70%'>[JSON/HTTPS]</div>" .->5
    5-. "<div>3. Forwards, without a token</div><div style='font-size: 70%'>[HTTPS + Bearer]</div>" .->6
    6-. "<div>4. Free slots from the cache<br />(database on a miss)</div><div style='font-size: 70%'>[StackExchange.Redis]</div>" .->11
    4-. "<div>5. Clicks Book while signed<br />out:<br />/bff/login?returnUrl=/book/{slug}</div><div style='font-size: 70%'>[JSON/HTTPS]</div>" .->5
    2-. "<div>6. Signs in, or signs up and<br />verifies their email</div><div style='font-size: 70%'>[HTTPS]</div>" .->12
    5-. "<div>7. Exchanges the code for<br />tokens</div><div style='font-size: 70%'>[OIDC code flow]</div>" .->12
    5-. "<div>8. Stores the session, sets<br />the session cookie</div><div style='font-size: 70%'>[EF Core / Npgsql]</div>" .->10
    4-. "<div>9. POST booking with the<br />cookie and X-CSRF header<br />(rate-limited per user)</div><div style='font-size: 70%'>[JSON/HTTPS]</div>" .->5
    5-. "<div>10. Forwards with the<br />client's access token</div><div style='font-size: 70%'>[HTTPS + Bearer]</div>" .->6
    6-. "<div>11. Inserts the booking and a<br />BookingCreated outbox event<br />in one transaction; an<br />overlap fails the exclusion<br />constraint and answers 409</div><div style='font-size: 70%'>[EF Core / Npgsql]</div>" .->8
    6-. "<div>12. Invalidates that day's<br />free slots</div><div style='font-size: 70%'>[StackExchange.Redis]</div>" .->11
    6-. "<div>13. Outbox relay delivers<br />BookingCreated, retrying<br />until accepted</div><div style='font-size: 70%'>[JSON/HTTPS]</div>" .->7
    7-. "<div>14. Records the event once<br />(by event ID), schedules the<br />24 h reminder</div><div style='font-size: 70%'>[EF Core / Npgsql]</div>" .->9
    7-. "<div>15. Sends the confirmation</div><div style='font-size: 70%'>[HTTPS]</div>" .->14
    14-. "<div>16. Confirmation email</div><div style='font-size: 70%'></div>" .->2

  end
```
<!-- /diagram -->

### Someone starts a business

<!-- diagram: OwnerSignUp -->
```mermaid
graph LR
  linkStyle default fill:#ffffff

  subgraph diagram ["Dynamic View: Booking SaaS"]
    style diagram fill:#ffffff,stroke:#ffffff

    subgraph 3 ["Booking SaaS"]
      style 3 fill:#ffffff,stroke:#0b4884,color:#0b4884

      12("<div style='font-weight: bold'>logto</div><div style='font-size: 70%; margin-top: 0px'>[Container: Logto]</div><div style='font-size: 80%; margin-top:10px'>Self-hosted sign-in and<br />sign-up for owners and<br />clients, with an owner or<br />client role on each user.<br />Email verification on sign-up<br />through an email connector.<br />The admin console runs as a<br />second instance<br />(logto-admin).</div>")
      style 12 fill:#438dd5,stroke:#2e6295,color:#ffffff
      4["<div style='font-weight: bold'>frontend</div><div style='font-size: 70%; margin-top: 0px'>[Container: React + Vite]</div><div style='font-size: 80%; margin-top:10px'>Owner dashboard (calendar,<br />services, staff, hours), the<br />'Start your business'<br />sign-up, the public booking<br />page /book/{slug} and the<br />client's own bookings.</div>"]
      style 4 fill:#438dd5,stroke:#2e6295,color:#ffffff
      5("<div style='font-weight: bold'>bff</div><div style='font-size: 70%; margin-top: 0px'>[Container: ASP.NET Core + YARP]</div><div style='font-size: 80%; margin-top:10px'>Backend for the React app.<br />Signs users in with Logto<br />(code flow, confidential<br />client), keeps their tokens<br />server-side and gives the<br />browser only an HttpOnly<br />session cookie. Forwards<br />/api/* to web with the user's<br />access token, refreshing it<br />when it expires. Requires an<br />X-CSRF header on /api, and<br />rate-limits it: per IP for<br />public pages, per user for<br />bookings. In Azure it also<br />serves the built React app.</div>")
      style 5 fill:#438dd5,stroke:#2e6295,color:#ffffff
      6("<div style='font-weight: bold'>web</div><div style='font-size: 70%; margin-top: 0px'>[Container: ASP.NET Core]</div><div style='font-size: 80%; margin-top:10px'>Businesses, services, staff,<br />hours, free slots and<br />bookings. Owns app-db. Tenant<br />isolation: every row has a<br />BusinessId, enforced by EF<br />Core query filters and<br />PostgreSQL row-level<br />security. The role in the<br />token decides what a user can<br />do. Writes events to an<br />outbox and relays them to<br />notifications. Reachable only<br />from bff.</div>")
      style 6 fill:#438dd5,stroke:#2e6295,color:#ffffff
      8[("<div style='font-weight: bold'>app-db</div><div style='font-size: 70%; margin-top: 0px'>[Container: PostgreSQL]</div><div style='font-size: 80%; margin-top:10px'>Businesses, services, staff,<br />opening hours, bookings and<br />the outbox. An exclusion<br />constraint on (staff, time<br />range) makes overlapping<br />bookings impossible.</div>")]
      style 8 fill:#438dd5,stroke:#2e6295,color:#ffffff
    end

    1["<div style='font-weight: bold'>Business owner</div><div style='font-size: 70%; margin-top: 0px'>[Person]</div><div style='font-size: 80%; margin-top:10px'>Runs a small business<br />(hairdresser, tutor, physio).<br />Sets up services, staff and<br />hours, manages bookings. The<br />customer of the SaaS.</div>"]
    style 1 fill:#08427b,stroke:#052e56,color:#ffffff

    1-. "<div>1. Opens 'Start your<br />business'</div><div style='font-size: 70%'>[HTTPS]</div>" .->4
    4-. "<div>2. Goes to<br />/bff/login?signup=true&returnUrl=/start</div><div style='font-size: 70%'>[JSON/HTTPS]</div>" .->5
    1-. "<div>3. Signs up and verifies<br />their email</div><div style='font-size: 70%'>[HTTPS]</div>" .->12
    5-. "<div>4. Exchanges the code for<br />tokens (no owner role yet)</div><div style='font-size: 70%'>[OIDC code flow]</div>" .->12
    4-. "<div>5. POST /api/businesses with<br />the name and slug</div><div style='font-size: 70%'>[JSON/HTTPS]</div>" .->5
    5-. "<div>6. Forwards with the user's<br />access token</div><div style='font-size: 70%'>[HTTPS + Bearer]</div>" .->6
    6-. "<div>7. Creates the business, with<br />this user as its owner</div><div style='font-size: 70%'>[EF Core / Npgsql]</div>" .->8
    6-. "<div>8. Assigns the owner role<br />(Management API,<br />machine-to-machine app)</div><div style='font-size: 70%'>[HTTPS]</div>" .->12
    5-. "<div>9. Refreshes the tokens, so<br />the owner role is in them</div><div style='font-size: 70%'>[OIDC code flow]</div>" .->12

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

      10[("<div style='font-weight: bold'>bff-db</div><div style='font-size: 70%; margin-top: 0px'>[Container: PostgreSQL]</div><div style='font-size: 80%; margin-top:10px'>Sessions with the users'<br />tokens, and the data<br />protection keys that encrypt<br />the session cookie. Survives<br />restarts and is shared by<br />every bff replica.</div>")]
      style 10 fill:#438dd5,stroke:#2e6295,color:#ffffff
      12("<div style='font-weight: bold'>logto</div><div style='font-size: 70%; margin-top: 0px'>[Container: Logto]</div><div style='font-size: 80%; margin-top:10px'>Self-hosted sign-in and<br />sign-up for owners and<br />clients, with an owner or<br />client role on each user.<br />Email verification on sign-up<br />through an email connector.<br />The admin console runs as a<br />second instance<br />(logto-admin).</div>")
      style 12 fill:#438dd5,stroke:#2e6295,color:#ffffff
      4["<div style='font-weight: bold'>frontend</div><div style='font-size: 70%; margin-top: 0px'>[Container: React + Vite]</div><div style='font-size: 80%; margin-top:10px'>Owner dashboard (calendar,<br />services, staff, hours), the<br />'Start your business'<br />sign-up, the public booking<br />page /book/{slug} and the<br />client's own bookings.</div>"]
      style 4 fill:#438dd5,stroke:#2e6295,color:#ffffff
      5("<div style='font-weight: bold'>bff</div><div style='font-size: 70%; margin-top: 0px'>[Container: ASP.NET Core + YARP]</div><div style='font-size: 80%; margin-top:10px'>Backend for the React app.<br />Signs users in with Logto<br />(code flow, confidential<br />client), keeps their tokens<br />server-side and gives the<br />browser only an HttpOnly<br />session cookie. Forwards<br />/api/* to web with the user's<br />access token, refreshing it<br />when it expires. Requires an<br />X-CSRF header on /api, and<br />rate-limits it: per IP for<br />public pages, per user for<br />bookings. In Azure it also<br />serves the built React app.</div>")
      style 5 fill:#438dd5,stroke:#2e6295,color:#ffffff
      6("<div style='font-weight: bold'>web</div><div style='font-size: 70%; margin-top: 0px'>[Container: ASP.NET Core]</div><div style='font-size: 80%; margin-top:10px'>Businesses, services, staff,<br />hours, free slots and<br />bookings. Owns app-db. Tenant<br />isolation: every row has a<br />BusinessId, enforced by EF<br />Core query filters and<br />PostgreSQL row-level<br />security. The role in the<br />token decides what a user can<br />do. Writes events to an<br />outbox and relays them to<br />notifications. Reachable only<br />from bff.</div>")
      style 6 fill:#438dd5,stroke:#2e6295,color:#ffffff
      8[("<div style='font-weight: bold'>app-db</div><div style='font-size: 70%; margin-top: 0px'>[Container: PostgreSQL]</div><div style='font-size: 80%; margin-top:10px'>Businesses, services, staff,<br />opening hours, bookings and<br />the outbox. An exclusion<br />constraint on (staff, time<br />range) makes overlapping<br />bookings impossible.</div>")]
      style 8 fill:#438dd5,stroke:#2e6295,color:#ffffff
    end

    1["<div style='font-weight: bold'>Business owner</div><div style='font-size: 70%; margin-top: 0px'>[Person]</div><div style='font-size: 80%; margin-top:10px'>Runs a small business<br />(hairdresser, tutor, physio).<br />Sets up services, staff and<br />hours, manages bookings. The<br />customer of the SaaS.</div>"]
    style 1 fill:#08427b,stroke:#052e56,color:#ffffff

    1-. "<div>1. Clicks Sign in</div><div style='font-size: 70%'>[HTTPS]</div>" .->4
    4-. "<div>2. Goes to /bff/login</div><div style='font-size: 70%'>[JSON/HTTPS]</div>" .->5
    1-. "<div>3. Redirected to Logto, signs<br />in</div><div style='font-size: 70%'>[HTTPS]</div>" .->12
    5-. "<div>4. Gets the code back on<br />/signin-oidc, exchanges it<br />for ID, access and refresh<br />tokens</div><div style='font-size: 70%'>[OIDC code flow]</div>" .->12
    5-. "<div>5. Stores the session with<br />the tokens, sets the session<br />cookie</div><div style='font-size: 70%'>[EF Core / Npgsql]</div>" .->10
    4-. "<div>6. GET /api/bookings with the<br />cookie and X-CSRF header</div><div style='font-size: 70%'>[JSON/HTTPS]</div>" .->5
    5-. "<div>7. Forwards with<br />Authorization: Bearer <access<br />token></div><div style='font-size: 70%'>[HTTPS + Bearer]</div>" .->6
    6-. "<div>8. Checks the token signature<br />(JWKS, cached)</div><div style='font-size: 70%'>[HTTPS]</div>" .->12
    6-. "<div>9. Reads only this business's<br />bookings (query filter and<br />row-level security)</div><div style='font-size: 70%'>[EF Core / Npgsql]</div>" .->8

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

    subgraph 36 ["Azure"]
      style 36 fill:#ffffff,stroke:#444444,color:#444444

      subgraph 37 ["Container Apps environment"]
        style 37 fill:#ffffff,stroke:#444444,color:#444444

        subgraph 38 ["bff"]
          style 38 fill:#ffffff,stroke:#444444,color:#444444

          39("<div style='font-weight: bold'>bff</div><div style='font-size: 70%; margin-top: 0px'>[Container: ASP.NET Core + YARP]</div><div style='font-size: 80%; margin-top:10px'>Backend for the React app.<br />Signs users in with Logto<br />(code flow, confidential<br />client), keeps their tokens<br />server-side and gives the<br />browser only an HttpOnly<br />session cookie. Forwards<br />/api/* to web with the user's<br />access token, refreshing it<br />when it expires. Requires an<br />X-CSRF header on /api, and<br />rate-limits it: per IP for<br />public pages, per user for<br />bookings. In Azure it also<br />serves the built React app.</div>")
          style 39 fill:#438dd5,stroke:#2e6295,color:#ffffff
          40["<div style='font-weight: bold'>frontend</div><div style='font-size: 70%; margin-top: 0px'>[Container: React + Vite]</div><div style='font-size: 80%; margin-top:10px'>Owner dashboard (calendar,<br />services, staff, hours), the<br />'Start your business'<br />sign-up, the public booking<br />page /book/{slug} and the<br />client's own bookings.</div>"]
          style 40 fill:#438dd5,stroke:#2e6295,color:#ffffff
        end

        subgraph 42 ["web"]
          style 42 fill:#ffffff,stroke:#444444,color:#444444

          43("<div style='font-weight: bold'>web</div><div style='font-size: 70%; margin-top: 0px'>[Container: ASP.NET Core]</div><div style='font-size: 80%; margin-top:10px'>Businesses, services, staff,<br />hours, free slots and<br />bookings. Owns app-db. Tenant<br />isolation: every row has a<br />BusinessId, enforced by EF<br />Core query filters and<br />PostgreSQL row-level<br />security. The role in the<br />token decides what a user can<br />do. Writes events to an<br />outbox and relays them to<br />notifications. Reachable only<br />from bff.</div>")
          style 43 fill:#438dd5,stroke:#2e6295,color:#ffffff
        end

        subgraph 45 ["notifications"]
          style 45 fill:#ffffff,stroke:#444444,color:#444444

          46("<div style='font-weight: bold'>notifications</div><div style='font-size: 70%; margin-top: 0px'>[Container: ASP.NET Core + Quartz.NET]</div><div style='font-size: 80%; margin-top:10px'>Owns notifications-db.<br />Receives booking events<br />(idempotent by event ID),<br />sends confirmations and<br />cancellations, and schedules<br />reminders 24 h before each<br />appointment with Quartz.NET.</div>")
          style 46 fill:#438dd5,stroke:#2e6295,color:#ffffff
        end

        subgraph 48 ["cache"]
          style 48 fill:#ffffff,stroke:#444444,color:#444444

          49[("<div style='font-weight: bold'>cache</div><div style='font-size: 70%; margin-top: 0px'>[Container: Redis]</div><div style='font-size: 80%; margin-top:10px'>Free slots per business and<br />day, invalidated when a<br />booking is made or cancelled.<br />Only a cache: losing it costs<br />a database query, nothing<br />else.</div>")]
          style 49 fill:#438dd5,stroke:#2e6295,color:#ffffff
        end

        subgraph 51 ["logto"]
          style 51 fill:#ffffff,stroke:#444444,color:#444444

          52("<div style='font-weight: bold'>logto</div><div style='font-size: 70%; margin-top: 0px'>[Container: Logto]</div><div style='font-size: 80%; margin-top:10px'>Self-hosted sign-in and<br />sign-up for owners and<br />clients, with an owner or<br />client role on each user.<br />Email verification on sign-up<br />through an email connector.<br />The admin console runs as a<br />second instance<br />(logto-admin).</div>")
          style 52 fill:#438dd5,stroke:#2e6295,color:#ffffff
        end

      end

      subgraph 55 ["PostgreSQL"]
        style 55 fill:#ffffff,stroke:#444444,color:#444444

        56[("<div style='font-weight: bold'>app-db</div><div style='font-size: 70%; margin-top: 0px'>[Container: PostgreSQL]</div><div style='font-size: 80%; margin-top:10px'>Businesses, services, staff,<br />opening hours, bookings and<br />the outbox. An exclusion<br />constraint on (staff, time<br />range) makes overlapping<br />bookings impossible.</div>")]
        style 56 fill:#438dd5,stroke:#2e6295,color:#ffffff
        58[("<div style='font-weight: bold'>notifications-db</div><div style='font-size: 70%; margin-top: 0px'>[Container: PostgreSQL]</div><div style='font-size: 80%; margin-top:10px'>What notifications needs to<br />remind people (appointment<br />time, recipient), the emails<br />sent, and the Quartz job<br />store.</div>")]
        style 58 fill:#438dd5,stroke:#2e6295,color:#ffffff
        60[("<div style='font-weight: bold'>bff-db</div><div style='font-size: 70%; margin-top: 0px'>[Container: PostgreSQL]</div><div style='font-size: 80%; margin-top:10px'>Sessions with the users'<br />tokens, and the data<br />protection keys that encrypt<br />the session cookie. Survives<br />restarts and is shared by<br />every bff replica.</div>")]
        style 60 fill:#438dd5,stroke:#2e6295,color:#ffffff
        62[("<div style='font-weight: bold'>logto-db</div><div style='font-size: 70%; margin-top: 0px'>[Container: PostgreSQL]</div><div style='font-size: 80%; margin-top:10px'>Logto's users, roles and<br />applications. Backed up with<br />the PostgreSQL server.</div>")]
        style 62 fill:#438dd5,stroke:#2e6295,color:#ffffff
      end

      64("<div style='font-weight: bold'>Key Vault</div><div style='font-size: 70%; margin-top: 0px'>[Infrastructure Node: Azure Key Vault]</div><div style='font-size: 80%; margin-top:10px'>Connection strings, Logto's<br />database URL, the Logto app<br />secret (bff), the Logto<br />Management API credentials<br />(web) and the email key, read<br />with managed identities.</div>")
      style 64 fill:#ffffff,stroke:#b2b2b2,color:#000000
      65("<div style='font-weight: bold'>Application Insights</div><div style='font-size: 70%; margin-top: 0px'>[Infrastructure Node: Azure Monitor]</div><div style='font-size: 80%; margin-top:10px'>Logs, traces and metrics from<br />the services.</div>")
      style 65 fill:#ffffff,stroke:#b2b2b2,color:#000000
    end

    40-. "<div>Calls /api with the session<br />cookie</div><div style='font-size: 70%'>[JSON/HTTPS]</div>" .->39
    39-. "<div>Forwards /api, with the<br />user's access token</div><div style='font-size: 70%'>[HTTPS + Bearer]</div>" .->43
    43-. "<div>Relays outbox events, retried<br />until accepted</div><div style='font-size: 70%'>[JSON/HTTPS]</div>" .->46
    43-. "<div>Caches free slots</div><div style='font-size: 70%'>[StackExchange.Redis]</div>" .->49
    39-. "<div>Signs users in, refreshes<br />their tokens</div><div style='font-size: 70%'>[OIDC code flow]</div>" .->52
    43-. "<div>Validates access tokens<br />(JWKS), assigns the owner<br />role (Management API)</div><div style='font-size: 70%'>[HTTPS]</div>" .->52
    43-. "<div>Reads and writes; a booking<br />and its outbox event in one<br />transaction</div><div style='font-size: 70%'>[EF Core / Npgsql]</div>" .->56
    46-. "<div>Reads and writes</div><div style='font-size: 70%'>[EF Core / Npgsql]</div>" .->58
    39-. "<div>Keeps sessions and data<br />protection keys</div><div style='font-size: 70%'>[EF Core / Npgsql]</div>" .->60
    52-. "<div>Reads and writes</div><div style='font-size: 70%'>[PostgreSQL]</div>" .->62

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
