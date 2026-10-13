# How booking works

The rules behind the tools of the Aspire Showcase bookings server. The tools enforce them through the API; this is so you can explain an answer, or a refusal, to the user.

## Businesses and booking links

- Every business has a booking link, its slug, such as `anna-hair`: the last part of its booking page's URL, `/book/anna-hair`. Tools take the link, not the name; `list_businesses` finds it from a name.
- A business offers services, each with a duration and a price. Prices are shown only: there's no online payment.
- Staff members do some of the services, within their own working hours, which stay within the business's opening hours. The owner is a staff member of their own business from the start.

## Free times

- Free times are offered for the next four weeks.
- A time is free when a staff member who does the service is working, has no other booking then, and isn't on time off. A business can add a buffer after a service, such as 10 minutes to clean up, which keeps the next booking further away.
- Without a staff member, "anyone" is the union of every suitable staff member's free times; booking one assigns whoever is free.
- Bookings for different staff can overlap; for the same staff member they never do.

## Times and time zones

- Every time a tool shows, a date (`yyyy-MM-dd`) or a time of day (`HH:mm`), is in the business's own time zone, which `get_business` and `owner_calendar` give. Say so when the user may be elsewhere.
- To book or move, pass the slot's `startsAt` exactly as a tool returned it: it's the instant, with its offset. Don't build one from a date and a time yourself.

## Booking

- A booking needs a signed-in user, and it's tied to their account. It's under the name and email they confirm, and the email gets the confirmation, with a calendar invite.
- If someone else took the time in the meantime, the API answers "This time was just taken": find the free times again and let the user pick another.

## Cancelling and moving, as a client

- A client can cancel or move their own upcoming bookings until the business's cancellation notice: for example, not less than 24 hours before. `get_business` gives the notice in hours, and `my_bookings` says until when each booking can still be changed. The notice is the one the booking was made under, even if the business changed it since.
- Inside the notice, the tools refuse, and the client has to contact the business: `my_bookings` gives its contact email.
- Moving keeps the booking until the new time is booked: if the new time is taken, the old one stays.
- Cancelling frees the time at once. The client and the owner get an email for every change.

## For the owner of a business

- Only a business's owner sees its calendar, with each client's name and email, and only their own business's. A user who isn't an owner is refused.
- The owner can cancel or move any booking of their business, at any time, regardless of the cancellation notice, and to another staff member. The client gets an email.
- The owner can book someone who phoned or walked in, under the client's name and email; the client needs no account, and gets the same confirmation.
- Time off blocks the whole business or one staff member, from a local start to a local end. Bookings already inside it stay: the answer lists them, and the owner decides about each, cancelling or moving it.

## Before anything changes

Every tool that books, cancels or moves, or adds or removes time off, asks the user to confirm in a form their client shows, and changes nothing if they decline. When the client can't show forms, ask the user yourself, and call the tool only once they agree.
