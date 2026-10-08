Proposed modules

1. Business Setup (supporting): what the owner configures

Aggregates:
Business: slug, time zone, opening hours, profile, languages, widget settings
Service: duration, buffer, price, hidden flag
StaffMember: services offered, working hours
Invariants:
The slug is unique.
Durations come in 5-minute steps.
Staff working hours fall within opening hours. This is why StaffMember and Business share a module: the rule crosses both.
The owner becomes a StaffMember when the business is created.
Publishes: BusinessCreated, ServiceChanged (duration, buffer, visibility), StaffWorkingHoursChanged, StaffServicesChanged, BusinessTimeZoneChanged.

2. Scheduling (core): bookings and availability

Aggregates:
Booking: staff, service, time range, attendee, status
TimeOff: business-wide or for one staff member
CancellationPolicy
In v2: WaitlistEntry and RecurringSeries
Domain service: AvailabilityCalculator. It combines staff working hours, time off, buffers and existing bookings into free slots, and also resolves "anyone".
Keeps local projections of staff hours, staff services, service durations and buffers, and time zones, built from Business Setup events. Slot calculation is the hot path, so it shouldn't call another module for every request.
The attendee is a value object, Attendee { UserId?, Name, Email }. That covers phone bookings with no account (V1-5) without a Clients module in the MVP.

3. Identity & Access (generic): an anti-corruption layer over Logto

Covers sign-up and sign-in, email verification, and roles per business (owner, and staff from v2).
Publishes AccountDeleted. Each module anonymizes its own data in response, rather than Identity reaching into other modules' tables.
For now Scheduling forgets a deleted client itself, in the same request that deletes the account (V1-8): it's the only module holding clients' data, and Identity has no outbox to publish AccountDeleted reliably. When a second module keeps personal data, AccountDeleted takes over.

4. Notifications (supporting): purely reactive

Subscribes to Scheduling events and sends the confirmation, reminder, owner and cancellation emails, plus .ics files. SMS comes in v2.
For "exactly one confirmation", use an idempotent consumer keyed by (BookingId, NotificationType), with the MassTransit outbox and inbox.
For reminders, schedule the message at confirmation time and check the booking's state when it fires. This is more robust than trying to unschedule a reminder on cancel or reschedule.

5. Clients (v2): client view per business

Builds no-show history and the frequent no-shows list (V2-5) from BookingMarkedNoShow and BookingConfirmed.
There's no reason to create it before v2.

6. Calendar Sync (v2, generic): anti-corruption layer over Google and Microsoft Graph

Pushes bookings out to external calendars.
Feeds external busy times into Scheduling as ExternalBusyBlock, so the calculator treats them like time off.
This is the module most likely to become a separate service later.

7. Insights (v2): read only

Projections or replica queries. No domain logic.

The public booking page is UI composition, not a module. It reads the profile and services from Business Setup and the free slots from Scheduling.

Story → module map
Module	Stories
Business Setup	MVP-8, 9, 10, 11 · V1-2 (buffer on Service) · V1-6 · V2-8, V2-9 (config)
Scheduling	MVP-1, 2, 4, 7, 12, 14 · V1-1, 3, 4, 5 · V2-4, V2-5 (marking), V2-6
Identity	MVP-3 · V1-8 (trigger) · V2-1
Notifications	MVP-5, 6, 13 · V1-7 · V2-3
Clients	V2-5 (history)
Calendar Sync	V2-2
Insights	V2-7
Design decisions worth making explicitly

Booking is a small aggregate, and Postgres enforces the overlap rule. A per-staff "calendar" aggregate would protect the invariant in the domain, but it becomes a contention hotspot and a huge object. Instead, use an exclusion constraint on (staff_id, occupied_range) with btree_gist. Then map the 23P01 exclusion violation to a domain result, SlotTaken. That gives MVP-4 its "This time was just taken" message instead of an exception page.

Make occupied_range include the buffer. Store start → end + buffer for the constraint, but show clients only start → end. Buffers (V1-2) then come almost for free, because the database already blocks back-to-back bookings.

Reschedule by updating the row, not insert-then-cancel. If a client moves a booking 30 minutes later with the same staff member, inserting the new booking first would collide with their own old booking. Updating the time range in place is atomic. It also satisfies "the old slot frees only once the new one is booked" (V1-4) without any extra logic.

Keep TimeOff out of the exclusion constraint. V1-1 requires that blocking time over existing bookings lists those bookings rather than being rejected. So time off belongs to the availability calculation, not the database constraint.

Plan for two kinds of tenant isolation in Scheduling. Owner and staff queries are scoped to one business, through a global query filter on BusinessId. "My bookings across all businesses" (MVP-7) is scoped to the user and crosses tenants on purpose. Give that one its own explicit query path, so nobody "fixes" it by disabling the filter globally.

The cancellation policy belongs to Scheduling, not Business Setup. The owner edits it in the settings screen, but Scheduling is the context that enforces it. Keeping the rule next to the decision avoids a synchronous cross-module check on every cancel.

Waitlist could become its own module in v2. It is mostly reactive: it consumes BookingCancelled, offers the slot with an expiry, and books on acceptance. If Scheduling grows too large, this is the first piece to extract.