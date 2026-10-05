/*
 * Proposed architecture: appointment booking SaaS built on the Aspire showcase.
 * Container names follow the AppHost resources (frontend, bff, web, notifications,
 * app-db, cache, logto) so the diagram and the code stay easy to compare.
 * The browser never holds a token: bff signs users in and keeps their tokens.
 */
workspace "Booking SaaS" "Proposed: appointment booking for small businesses, built on the Aspire showcase." {

    !identifiers hierarchical

    model {
        owner = person "Business owner" "Runs a small business (hairdresser, tutor, physio). Sets up services, staff and hours, manages bookings. The customer of the SaaS."
        client = person "Client" "Signs in to book an appointment from the business's public page, and sees or cancels their own bookings."

        booking = softwareSystem "Booking SaaS" "Owner dashboard and public booking pages." {
            frontend = container "frontend" "Owner dashboard (calendar, services, staff, hours), the 'Start your business' sign-up, the public booking page /book/{slug} and the client's own bookings." "React + Vite" "Web Browser"
            bff = container "bff" "Backend for the React app. Signs users in with Logto (code flow, confidential client), keeps their tokens server-side and gives the browser only an HttpOnly session cookie. Forwards /api/* to web with the user's access token, refreshing it when it expires. Requires an X-CSRF header on /api, and rate-limits it: per IP for public pages, per user for bookings. In Azure it also serves the built React app." "ASP.NET Core + YARP"
            web = container "web" "Businesses, services, staff, hours, free slots and bookings. Owns app-db. Tenant isolation: every row has a BusinessId, enforced by EF Core query filters and PostgreSQL row-level security. The role in the token decides what a user can do. Writes events to an outbox and relays them to notifications. Reachable only from bff." "ASP.NET Core"
            notifications = container "notifications" "Owns notifications-db. Receives booking events (idempotent by event ID), sends confirmations and cancellations, and schedules reminders 24 h before each appointment with Quartz.NET." "ASP.NET Core + Quartz.NET"
            appDb = container "app-db" "Businesses, services, staff, opening hours, bookings and the outbox. An exclusion constraint on (staff, time range) makes overlapping bookings impossible." "PostgreSQL" "Database"
            notificationsDb = container "notifications-db" "What notifications needs to remind people (appointment time, recipient), the emails sent, and the Quartz job store." "PostgreSQL" "Database"
            bffDb = container "bff-db" "Sessions with the users' tokens, and the data protection keys that encrypt the session cookie. Survives restarts and is shared by every bff replica." "PostgreSQL" "Database"
            cache = container "cache" "Free slots per business and day, invalidated when a booking is made or cancelled. Only a cache: losing it costs a database query, nothing else." "Redis" "Database"
            logto = container "logto" "Self-hosted sign-in and sign-up for owners and clients, with an owner or client role on each user. Email verification on sign-up through an email connector. The admin console runs as a second instance (logto-admin)." "Logto"
            logtoDb = container "logto-db" "Logto's users, roles and applications. Backed up with the PostgreSQL server." "PostgreSQL" "Database"
        }

        email = softwareSystem "Email service" "Sends transactional email (e.g. Azure Communication Services Email)." "External"

        # People
        owner -> booking.frontend "Sets up their business, manages bookings" "HTTPS"
        client -> booking.frontend "Picks a free slot and books" "HTTPS"
        owner -> booking.logto "Signs up and signs in" "HTTPS"
        client -> booking.logto "Signs up and signs in" "HTTPS"
        email -> client "Confirmation and reminder"
        email -> owner "New booking"

        # Inside the system
        booking.frontend -> booking.bff "Calls /api with the session cookie" "JSON/HTTPS"
        booking.bff -> booking.logto "Signs users in, refreshes their tokens" "OIDC code flow"
        booking.bff -> booking.web "Forwards /api, with the user's access token" "HTTPS + Bearer"
        booking.bff -> booking.bffDb "Keeps sessions and data protection keys" "EF Core / Npgsql"
        booking.web -> booking.logto "Validates access tokens (JWKS), assigns the owner role (Management API)" "HTTPS"
        booking.web -> booking.appDb "Reads and writes; a booking and its outbox event in one transaction" "EF Core / Npgsql"
        booking.web -> booking.cache "Caches free slots" "StackExchange.Redis"
        booking.web -> booking.notifications "Relays outbox events, retried until accepted" "JSON/HTTPS"
        booking.notifications -> booking.notificationsDb "Reads and writes" "EF Core / Npgsql"
        booking.notifications -> email "Sends email" "HTTPS"
        booking.logto -> booking.logtoDb "Reads and writes" "PostgreSQL"
        booking.logto -> email "Sends verification codes" "HTTPS"

        production = deploymentEnvironment "Azure" {
            deploymentNode "Azure" "" "Microsoft Azure" {
                deploymentNode "Container Apps environment" "aca-env" "Azure Container Apps" {
                    deploymentNode "bff" "Public ingress" "Container App" {
                        containerInstance booking.bff
                        containerInstance booking.frontend
                    }
                    deploymentNode "web" "Internal ingress" "Container App" {
                        containerInstance booking.web
                    }
                    deploymentNode "notifications" "Internal ingress" "Container App" {
                        containerInstance booking.notifications
                    }
                    deploymentNode "cache" "Internal" "Container App" {
                        containerInstance booking.cache
                    }
                    deploymentNode "logto" "Public ingress; logto-admin is a second Container App for the admin console" "Container App" {
                        containerInstance booking.logto
                    }
                }
                deploymentNode "PostgreSQL" "One server, a database per service" "Azure Database for PostgreSQL Flexible Server" {
                    containerInstance booking.appDb
                    containerInstance booking.notificationsDb
                    containerInstance booking.bffDb
                    containerInstance booking.logtoDb
                }
                keyVault = infrastructureNode "Key Vault" "Connection strings, Logto's database URL, the Logto app secret (bff), the Logto Management API credentials (web) and the email key, read with managed identities." "Azure Key Vault"
                appInsights = infrastructureNode "Application Insights" "Logs, traces and metrics from the services." "Azure Monitor"
            }
        }
    }

    views {
        systemContext booking "Context" "Who uses the booking SaaS and what it depends on." {
            include *
            autoLayout lr
        }

        container booking "Containers" "The containers, named as in the AppHost." {
            include *
            autoLayout lr
        }

        dynamic booking "BookASlot" "A client browses free slots without signing in, then signs in to book one." {
            client -> booking.frontend "Opens /book/{slug} and picks a slot"
            booking.frontend -> booking.bff "GET free slots (public, rate-limited per IP)"
            booking.bff -> booking.web "Forwards, without a token"
            booking.web -> booking.cache "Free slots from the cache (database on a miss)"
            booking.frontend -> booking.bff "Clicks Book while signed out: /bff/login?returnUrl=/book/{slug}"
            client -> booking.logto "Signs in, or signs up and verifies their email"
            booking.bff -> booking.logto "Exchanges the code for tokens"
            booking.bff -> booking.bffDb "Stores the session, sets the session cookie"
            booking.frontend -> booking.bff "POST booking with the cookie and X-CSRF header (rate-limited per user)"
            booking.bff -> booking.web "Forwards with the client's access token"
            booking.web -> booking.appDb "Inserts the booking and a BookingCreated outbox event in one transaction; an overlap fails the exclusion constraint and answers 409"
            booking.web -> booking.cache "Invalidates that day's free slots"
            booking.web -> booking.notifications "Outbox relay delivers BookingCreated, retrying until accepted"
            booking.notifications -> booking.notificationsDb "Records the event once (by event ID), schedules the 24 h reminder"
            booking.notifications -> email "Sends the confirmation"
            email -> client "Confirmation email"
            autoLayout lr
        }

        dynamic booking "OwnerSignUp" "Someone starts a business: they sign up as a user, then web makes them an owner." {
            owner -> booking.frontend "Opens 'Start your business'"
            booking.frontend -> booking.bff "Goes to /bff/login?signup=true&returnUrl=/start"
            owner -> booking.logto "Signs up and verifies their email"
            booking.bff -> booking.logto "Exchanges the code for tokens (no owner role yet)"
            booking.frontend -> booking.bff "POST /api/businesses with the name and slug"
            booking.bff -> booking.web "Forwards with the user's access token"
            booking.web -> booking.appDb "Creates the business, with this user as its owner"
            booking.web -> booking.logto "Assigns the owner role (Management API, machine-to-machine app)"
            booking.bff -> booking.logto "Refreshes the tokens, so the owner role is in them"
            autoLayout lr
        }

        dynamic booking "OwnerSignIn" "An owner signs in and opens the dashboard. Tokens stay in bff." {
            owner -> booking.frontend "Clicks Sign in"
            booking.frontend -> booking.bff "Goes to /bff/login"
            owner -> booking.logto "Redirected to Logto, signs in"
            booking.bff -> booking.logto "Gets the code back on /signin-oidc, exchanges it for ID, access and refresh tokens"
            booking.bff -> booking.bffDb "Stores the session with the tokens, sets the session cookie"
            booking.frontend -> booking.bff "GET /api/bookings with the cookie and X-CSRF header"
            booking.bff -> booking.web "Forwards with Authorization: Bearer <access token>"
            booking.web -> booking.logto "Checks the token signature (JWKS, cached)"
            booking.web -> booking.appDb "Reads only this business's bookings (query filter and row-level security)"
            autoLayout lr
        }

        deployment booking "Azure" "AzureDeployment" "What aspire deploy creates in Azure. The email service is outside it." {
            include *
            autoLayout lr
        }

        styles {
            element "Element" {
                shape roundedbox
            }
            element "Person" {
                shape person
                background #08427b
                color #ffffff
            }
            element "Software System" {
                background #1168bd
                color #ffffff
            }
            element "Container" {
                background #438dd5
                color #ffffff
            }
            element "Database" {
                shape cylinder
            }
            element "Web Browser" {
                shape webbrowser
            }
            element "External" {
                background #999999
                color #ffffff
            }
            element "Infrastructure Node" {
                background #ffffff
                color #000000
            }
        }
    }

}
