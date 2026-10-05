/*
 * Proposed architecture: appointment booking SaaS built on the Aspire showcase.
 * Container names match the AppHost resources (frontend, web, notifications,
 * app-db, cache, logto) so the diagram and the code stay easy to compare.
 */
workspace "Booking SaaS" "Proposed: appointment booking for small businesses, built on the Aspire showcase." {

    !identifiers hierarchical

    model {
        owner = person "Business owner" "Runs a small business (hairdresser, tutor, physio). Sets up services and hours, manages bookings. The paying customer."
        client = person "Client" "Books an appointment from the business's public page. No account."

        booking = softwareSystem "Booking SaaS" "Owner dashboard and public booking pages." {
            frontend = container "frontend" "Owner dashboard (calendar, services, hours, plan) and the public booking page /book/{slug}." "React + Vite" "Web Browser"
            web = container "web" "Businesses, services, hours, free slots, bookings and plan limits. Multi-tenant: every row belongs to a business. In Azure it also serves the built React app." "ASP.NET Core"
            notifications = container "notifications" "Booking confirmations and cancellations, and a Quartz.NET job that sends reminders 24 h before each appointment." "ASP.NET Core + Quartz.NET"
            appDb = container "app-db" "Businesses, services, opening hours, bookings, subscriptions. Quartz job store." "PostgreSQL" "Database"
            cache = container "cache" "Free slots per business and day (invalidated on booking), and a short lock per slot so two clients can't book the same one." "Redis" "Database"
            logto = container "logto" "Sign-in for business owners. Clients never sign in." "Logto"
        }

        stripe = softwareSystem "Stripe" "Subscriptions for the paid plan: checkout, customer portal, webhooks." "External"
        email = softwareSystem "Email service" "Sends transactional email (e.g. Azure Communication Services Email)." "External"

        # People
        owner -> booking.frontend "Manages services, hours and bookings" "HTTPS"
        client -> booking.frontend "Picks a free slot and books" "HTTPS"
        owner -> booking.logto "Signs in" "OIDC"
        owner -> stripe "Pays for the subscription" "Stripe Checkout"
        email -> client "Confirmation and reminder"
        email -> owner "New booking"

        # Inside the system
        booking.frontend -> booking.logto "Gets tokens for the owner" "OIDC + PKCE"
        booking.frontend -> booking.web "Calls" "JSON/HTTPS"
        booking.web -> booking.logto "Validates owner tokens" "JWKS"
        booking.web -> booking.appDb "Reads and writes" "EF Core / Npgsql"
        booking.web -> booking.cache "Caches free slots, locks a slot while booking" "StackExchange.Redis"
        booking.web -> booking.notifications "Booking created or cancelled" "JSON/HTTPS"
        booking.notifications -> booking.appDb "Reads upcoming bookings, stores Quartz jobs" "Npgsql"
        booking.notifications -> email "Sends email" "HTTPS"
        booking.web -> stripe "Creates checkout and portal sessions" "HTTPS"
        stripe -> booking.web "Subscription changed" "Webhook"

        production = deploymentEnvironment "Azure" {
            deploymentNode "Azure" "" "Microsoft Azure" {
                deploymentNode "Container Apps environment" "aca-env" "Azure Container Apps" {
                    deploymentNode "web" "" "Container App" {
                        containerInstance booking.web
                        containerInstance booking.frontend
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
                keyVault = infrastructureNode "Key Vault" "Connection strings and secrets (Stripe, email), read with managed identities." "Azure Key Vault"
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
            booking.frontend -> booking.web "GET free slots"
            booking.web -> booking.cache "Free slots from the cache (database on a miss)"
            booking.frontend -> booking.web "POST booking"
            booking.web -> booking.cache "Locks the slot"
            booking.web -> booking.appDb "Inserts the booking (unique business + start time)"
            booking.web -> booking.notifications "Booking created"
            booking.notifications -> email "Sends the confirmation"
            email -> client "Confirmation email"
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
