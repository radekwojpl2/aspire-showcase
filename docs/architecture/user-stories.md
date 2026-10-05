# Booking SaaS: user stories

User stories for the booking SaaS in [the proposed architecture](README.md), in three phases. Each phase is usable on its own: the MVP lets a small business and its staff take bookings, v1 makes it work for the day-to-day of a real shop, and v2 adds what keeps businesses on the platform.

Stories marked ✅ are built; 🟡 means partly built, with ✅ and ❌ on each acceptance criterion. So far: 3 of 14 MVP stories (MVP-8, MVP-9, MVP-10) and part of MVP-11, none of v1 or v2.

People in the stories:

- **Visitor**: anyone on a business's booking page, not signed in.
- **Client**: a signed-in user who books appointments.
- **Owner**: runs a business on the platform (has the owner role).
- **Staff member**: works at a business and is booked for appointments (signs in from v2).

## MVP: take bookings online

Goal: a small business with one or more staff can share a link, and clients book with the person they want and get reminded, with no double bookings.

### Clients

**MVP-1. See free slots without an account**
As a visitor, I want to see a business's services and free slots on its booking page, so that I can check availability before signing up.
- `/book/{slug}` shows the services and the free slots for the next 4 weeks, without signing in.
- Slots already booked, and times outside the staff's working hours, aren't shown.

**MVP-2. Choose a staff member**
As a client, I want to pick a staff member or "anyone", so that I can book with the person I prefer.
- Only staff who do the chosen service are offered.
- "Anyone" shows the union of their free slots, and assigns whoever is free.

**MVP-3. Sign up to book**
As a visitor, I want to create an account or sign in when I choose a slot, so that my booking is tied to me.
- Choosing **Book** while signed out goes to sign-in and comes back to the same slot.
- Sign-up verifies the email address before the account can book.

**MVP-4. Book a slot**
As a client, I want to book a free slot for a service, so that I have an appointment.
- The booking is saved for me, with the service, the staff member, and the start and end time.
- If someone else just took an overlapping time, I see "This time was just taken" and the updated free slots, not an error page.

**MVP-5. Get a confirmation**
As a client, I want a confirmation email when I book, so that I know the booking went through.
- The email has the business, service, date and time, and a link to my bookings.
- I get exactly one confirmation, even if sending is retried.

**MVP-6. Get a reminder**
As a client, I want a reminder email 24 hours before my appointment, so that I don't forget it.
- Bookings made less than 24 hours ahead get no reminder.
- A cancelled booking sends no reminder.

**MVP-7. See and cancel my bookings**
As a client, I want to see my upcoming bookings and cancel one, so that I can free a time I can't make.
- **My bookings** lists my upcoming bookings across all businesses.
- Cancelling frees the slot straight away and sends me and the owner an email.

### Owners

**MVP-8. Start a business** ✅
As someone with a small business, I want to sign up and create my business with a name and a booking link, so that I can start taking bookings.
- **Start your business** creates an account (if needed) and the business, and gives me the owner role.
- The link `/book/{slug}` must be unique; I'm told if it's taken.

**MVP-9. Set opening hours** ✅
As an owner, I want to set my business's weekly opening hours, so that clients can only book when we're open.
- Hours per weekday, with more than one range per day (e.g. 9–12 and 13–17).
- Changing hours doesn't cancel existing bookings.

**MVP-10. Add services** ✅
As an owner, I want to add the services I offer with a duration and a price, so that clients book the right length of time.
- Name, duration (in 5-minute steps) and price shown to clients (no online payment).
- A service can be hidden without deleting it.

**MVP-11. Add staff** 🟡
As an owner, I want to add staff members with the services they do and their own working hours, so that clients can be booked with them in parallel.
- ✅ I'm a staff member of my own business from the start, so a one-person business needs no setup here.
- ✅ Working hours stay within the opening hours.
- ❌ Bookings for different staff can overlap in time; for the same staff member they can't. Needs bookings: comes with MVP-4.

**MVP-12. See my bookings**
As an owner, I want to see my bookings by day and week, for everyone or one staff member, so that I can plan the work.
- Day and week views, with the client's name, email, service and staff member.
- I only ever see my own business's bookings.

**MVP-13. Hear about new bookings**
As an owner, I want an email when a client books or cancels, so that I don't have to keep checking.

**MVP-14. Cancel a booking**
As an owner, I want to cancel a client's booking, so that I can handle sickness or emergencies.
- The client gets an email; the slot becomes free.

### Done for every MVP story

- No two bookings for the same staff member overlap (enforced by the database).
- A business's data is never visible to another business.
- Times are stored in UTC and shown in the business's time zone.

## v1: run a real shop

Goal: businesses with holidays, cancellation rules and phone bookings can run their whole calendar here.

**V1-1. Block time off**
As an owner, I want to block holidays and breaks for the business or one staff member, so that nobody books me when I'm away.
- Blocking a time with bookings in it lists those bookings so I can cancel or move them.

**V1-2. Buffer between appointments**
As an owner, I want a buffer time after a service (e.g. 10 minutes to clean up), so that appointments don't run back to back.

**V1-3. Cancellation policy**
As an owner, I want to stop clients cancelling less than N hours before the appointment, so that I'm not left with gaps I can't fill.
- Clients see the policy before booking; inside the window, **Cancel** is replaced by the business's contact details.

**V1-4. Reschedule**
As a client, I want to move my booking to another free time, so that I don't have to cancel and book again.
- Same rules as cancelling; the old slot frees only once the new one is booked.

**V1-5. Book for a client**
As an owner, I want to add a booking myself for someone who phoned, so that all bookings are in one calendar.
- The client can be a name and email without an account; they get the confirmation and reminder.

**V1-6. Business page**
As an owner, I want my booking page to show my address, description and logo, so that clients know they're in the right place.

**V1-7. Add to calendar**
As a client, I want to add my booking to my calendar, so that it shows up next to everything else.
- The confirmation email has an `.ics` attachment, updated on reschedule or cancel.

**V1-8. Delete my account**
As a client, I want to delete my account and my data, so that I'm in control of my personal data.
- Past bookings stay for the business, without my name and email.

## v2: keep businesses coming back

Goal: fewer no-shows, less admin, and insight that makes owners stay.

**V2-1. Staff sign in**
As a staff member, I want to sign in and see my own schedule, so that I don't depend on the owner to tell me.
- A staff role in Logto; staff see and manage only their own bookings.

**V2-2. Calendar sync**
As a staff member, I want my bookings in my Google or Outlook calendar, and my busy times there blocked here, so that I'm never double-booked across calendars.

**V2-3. SMS reminders**
As an owner, I want reminders sent by SMS as well, so that fewer clients forget.

**V2-4. Waitlist**
As a client, I want to join a waitlist for a full day, so that I can get a slot if someone cancels.
- On a cancellation, the first person on the list is offered the slot for a limited time.

**V2-5. No-shows**
As an owner, I want to mark a booking as a no-show, and see clients who often don't come, so that I can handle them.

**V2-6. Recurring bookings**
As a client, I want to book the same time every week or every few weeks, so that I don't have to rebook each time.

**V2-7. Insights**
As an owner, I want to see bookings per week, busiest times, popular services and the no-show rate, so that I can plan staff and hours.

**V2-8. Booking widget**
As an owner, I want to put the booking page on my own website, so that clients book without leaving it.

**V2-9. Languages**
As a client, I want the booking page in my language, so that I understand it.
- The owner picks the languages; dates and times follow the client's locale.

## What each phase adds to the architecture

| Phase | Changes |
|---|---|
| MVP | What the diagrams show: staff, their services and working hours are tables in `app-db`, and the overlap constraint works per staff member. |
| v1 | No new services: time off, buffers and policies are more tables in `app-db`, and free slots take them into account. `.ics` files come from `notifications`. |
| v2 | A staff role in Logto; an SMS provider and calendar APIs (Google, Microsoft Graph) used by `notifications`, or a new sync service; reporting queries that may need a read replica. |
