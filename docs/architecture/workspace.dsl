/*
 * Proposed architecture: appointment booking SaaS built on the Aspire showcase.
 * Container names match the AppHost resources (frontend, bff, web, notifications,
 * app-db, cache, logto) so the diagram and the code stay easy to compare.
 * The browser never holds a token: bff signs owners in and keeps their tokens.
 */
workspace "Booking SaaS" "Proposed: appointment booking for small businesses, built on the Aspire showcase." {

    !identifiers hierarchical

    model {
        owner = person "Business owner" "Runs a small business (hairdresser, tutor, physio). Sets up services and hours, manages bookings. The customer of the SaaS."
        client = person "Client" "Books an appointment from the business's public page. No account."

        booking = softwareSystem "Booking SaaS" "Owner dashboard and public booking pages." {
            frontend = container "frontend" "Owner dashboard (calendar, services, hours) and the public booking page /book/{slug}." "React + Vite" "Web Browser"
            bff = container "bff" "Backend for the React app. Signs owners in with Logto (code flow, confidential client), keeps their tokens server-side and gives the browser only an HttpOnly session cookie. Forwards /api/* to web, adding the owner's access token, and refreshes it when it expires. Requires an X-CSRF header on /api. In Azure it also serves the built React app." "ASP.NET Core + YARP"
            web = container "web" "Businesses, services, hours, free slots and bookings. Multi-tenant: every row belongs to a business. Reachable only from bff; accepts Logto access tokens." "ASP.NET Core"
            notifications = container "notifications" "Booking confirmations and cancellations, and a Quartz.NET job that sends reminders 24 h before each appointment." "ASP.NET Core + Quartz.NET"
            appDb = container "app-db" "Businesses, services, opening hours, bookings. Quartz job store." "PostgreSQL" "Database"
            cache = container "cache" "Free slots per business and day (invalidated on booking), a short lock per slot so two clients can't book the same one, and bff's sessions and data protection keys." "Redis" "Database"
            logto = container "logto" "Sign-in for business owners. Clients never sign in." "Logto"
        }

        email = softwareSystem "Email service" "Sends transactional email (e.g. Azure Communication Services Email)." "External"

        # People
        owner -> booking.frontend "Manages services, hours and bookings" "HTTPS"
        client -> booking.frontend "Picks a free slot and books" "HTTPS"
        owner -> booking.logto "Signs in on Logto's page" "HTTPS"
        email -> client "Confirmation and reminder"
        email -> owner "New booking"

        # Inside the system
        booking.frontend -> booking.bff "Calls /api with the session cookie" "JSON/HTTPS"
        booking.bff -> booking.logto "Signs owners in, refreshes their tokens" "OIDC code flow"
        booking.bff -> booking.web "Forwards /api, with the owner's access token" "HTTPS + Bearer"
        booking.bff -> booking.cache "Keeps sessions and data protection keys" "StackExchange.Redis"
        booking.web -> booking.logto "Validates owner tokens" "JWKS"
        booking.web -> booking.appDb "Reads and writes" "EF Core / Npgsql"
        booking.web -> booking.cache "Caches free slots, locks a slot while booking" "StackExchange.Redis"
        booking.web -> booking.notifications "Booking created or cancelled" "JSON/HTTPS"
        booking.notifications -> booking.appDb "Reads upcoming bookings, stores Quartz jobs" "Npgsql"
        booking.notifications -> email "Sends email" "HTTPS"

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
                    deploymentNode "notifications" "" "Container App" {
                        containerInstance booking.notifications
                    }
                    deploymentNode "cache" "" "Container App" {
                        containerInstance booking.cache
                    }
                    deploymentNode "logto" "" "Container App" {
                        containerInstance booking.logto
                    }
                }
                deploymentNode "PostgreSQL" "" "Azure Database for PostgreSQL Flexible Server" {
                    containerInstance booking.appDb
                }
                keyVault = infrastructureNode "Key Vault" "Connection strings and secrets (email), read with managed identities." "Azure Key Vault"
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

        dynamic booking "BookASlot" "A client books a slot on the public page." {
            client -> booking.frontend "Opens /book/{slug} and picks a slot"
            booking.frontend -> booking.bff "GET free slots (no session needed)"
            booking.bff -> booking.web "Forwards, without a token"
            booking.web -> booking.cache "Free slots from the cache (database on a miss)"
            booking.frontend -> booking.bff "POST booking"
            booking.bff -> booking.web "Forwards"
            booking.web -> booking.cache "Locks the slot"
            booking.web -> booking.appDb "Inserts the booking (unique business + start time)"
            booking.web -> booking.notifications "Booking created"
            booking.notifications -> email "Sends the confirmation"
            email -> client "Confirmation email"
            autoLayout lr
        }

        dynamic booking "OwnerSignIn" "An owner signs in and opens the dashboard. Tokens stay in bff." {
            owner -> booking.frontend "Clicks Sign in"
            booking.frontend -> booking.bff "Goes to /bff/login"
            owner -> booking.logto "Redirected to Logto, signs in"
            booking.bff -> booking.logto "Gets the code back on /signin-oidc, exchanges it for ID, access and refresh tokens"
            booking.bff -> booking.cache "Stores the session with the tokens, sets the session cookie"
            booking.frontend -> booking.bff "GET /api/bookings with the cookie and X-CSRF header"
            booking.bff -> booking.web "Forwards with Authorization: Bearer <access token>"
            booking.web -> booking.logto "Checks the token signature (JWKS, cached)"
            autoLayout lr
        }

        deployment booking "Azure" "AzureDeployment" "What aspire deploy creates in Azure." {
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
