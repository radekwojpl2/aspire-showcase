import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { apiFetch, readProblem } from './api.ts';
import { useMyBusiness } from './my-business.ts';
import { describePolicy } from './policy-text.ts';
import { signInUrl, useSession } from './session.ts';
import { ErrorMessage } from './ui.tsx';

type Staff = { id: string; name: string };

type Service = {
  id: string;
  name: string;
  durationMinutes: number;
  price: number;
  currency: string;
  staff: Staff[];
};

type Business = {
  name: string;
  slug: string;
  timeZone: string;
  cancellationNoticeHours: number;
  services: Service[];
  // V1-6: null when the owner hasn't set them.
  address: string | null;
  description: string | null;
  logoUrl: string | null;
};

// Local date and time in the business's time zone, and the instant to book it with.
type Slot = { start: string; startsAt: string };
type Day = { date: string; slots: Slot[] };

type Confirmation = { serviceName: string; staffName: string; date: string; start: string; end: string };

const weeks = 4;

const parse = (day: string) => new Date(`${day}T00:00:00Z`);
const addDays = (day: string, days: number) => {
  const date = parse(day);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
};
const dayLabel = (day: string) =>
  parse(day).toLocaleDateString(undefined, { weekday: 'short', day: 'numeric', month: 'short', timeZone: 'UTC' });
const addMinutes = (time: string, minutes: number) => {
  const total = Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5)) + minutes;
  return `${String(Math.floor(total / 60) % 24).padStart(2, '0')}:${String(total % 60).padStart(2, '0')}`;
};
const price = (service: Service) =>
  new Intl.NumberFormat(undefined, { style: 'currency', currency: service.currency }).format(service.price);
const duration = (minutes: number) =>
  [Math.floor(minutes / 60) && `${Math.floor(minutes / 60)} h`, minutes % 60 && `${minutes % 60} min`]
    .filter(Boolean)
    .join(' ');

// Today in the business's time zone, as yyyy-MM-dd.
const todayIn = (timeZone: string) => new Intl.DateTimeFormat('en-CA', { timeZone }).format(new Date());

// The choice lives in the URL, so signing in can come back to the same slot (MVP-3).
function readChoice() {
  const query = new URLSearchParams(window.location.search);
  return { service: query.get('service') ?? '', staff: query.get('staff') ?? '', start: query.get('start') ?? '' };
}

function writeChoice(service: string, staff: string, start: string) {
  const query = new URLSearchParams();
  if (service) query.set('service', service);
  if (staff) query.set('staff', staff);
  if (start) query.set('start', start);
  const search = query.toString();
  window.history.replaceState(null, '', `${window.location.pathname}${search ? `?${search}` : ''}`);
}

// User stories MVP-1 to MVP-4: anyone sees the free slots; signing in books one.
export function BookingPage({ slug }: { slug: string }) {
  const { signInEnabled, user } = useSession();
  // V1-6: the owner sees a way to change what this page says about them.
  const { business: mine } = useMyBusiness(user !== null);
  const initial = readChoice();
  const [business, setBusiness] = useState<Business | null>();
  const [serviceId, setServiceId] = useState(initial.service);
  const [staffId, setStaffId] = useState(initial.staff);
  const [days, setDays] = useState<Day[]>();
  const [loadingSlots, setLoadingSlots] = useState(false);
  const [week, setWeek] = useState(0);
  const [startsAt, setStartsAt] = useState(initial.start);
  const [clientName, setClientName] = useState(user?.name ?? '');
  const [clientEmail, setClientEmail] = useState(user?.email ?? '');
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [error, setError] = useState<string>();
  const [booking, setBooking] = useState(false);
  const [booked, setBooked] = useState<Confirmation>();

  useEffect(() => {
    let current = true;
    apiFetch(`/api/public/businesses/${encodeURIComponent(slug)}`)
      .then(async (response) => {
        if (!current) return;
        if (response.status === 404) return setBusiness(null);
        if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);
        const found = (await response.json()) as Business;
        setBusiness(found);
        // Without a choice in the URL, start with the first service.
        setServiceId((chosen) =>
          found.services.some((service) => service.id === chosen) ? chosen : (found.services[0]?.id ?? ''),
        );
      })
      .catch((err) => current && setError(err instanceof Error ? err.message : 'Failed to call the API'));
    return () => {
      current = false;
    };
  }, [slug]);

  // isCurrent: false once another choice was made, so a slow answer can't show the wrong service's times.
  const loadSlots = useCallback(async (isCurrent: () => boolean = () => true) => {
    if (!serviceId) return;
    const query = new URLSearchParams({ serviceId });
    if (staffId) query.set('staffMemberId', staffId);
    const response = await apiFetch(`/api/public/businesses/${encodeURIComponent(slug)}/slots?${query}`);
    if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);
    const found = ((await response.json()) as { days: Day[] }).days;
    if (isCurrent()) setDays(found);
  }, [slug, serviceId, staffId]);

  // The previous times stay on screen, disabled, until the new ones come: clearing them would
  // collapse the calendar and make the page jump on every change of service or staff.
  useEffect(() => {
    let current = true;
    setLoadingSlots(true);
    loadSlots(() => current)
      .catch((err) => current && setError(err instanceof Error ? err.message : 'Failed to call the API'))
      .finally(() => current && setLoadingSlots(false));
    return () => {
      current = false;
    };
  }, [loadSlots]);

  useEffect(() => writeChoice(serviceId, staffId, startsAt), [serviceId, staffId, startsAt]);

  if (business === null) {
    return (
      <section className="card">
        <p className="hint">There's no business at this link.</p>
      </section>
    );
  }
  if (!business) return error ? <ErrorMessage message={error} /> : <p className="status" role="status">Loading...</p>;

  const service = business.services.find((candidate) => candidate.id === serviceId);
  const staffMember = service?.staff.find((member) => member.id === staffId);
  const today = todayIn(business.timeZone);
  const firstDay = addDays(today, week * 7);
  const shownDays = Array.from({ length: 7 }, (_, index) => addDays(firstDay, index));
  const slotsOn = (day: string) => days?.find((candidate) => candidate.date === day)?.slots ?? [];
  const chosen = days?.flatMap((day) => day.slots.map((slot) => ({ ...slot, date: day.date })))
    .find((slot) => slot.startsAt === startsAt);
  const returnUrl = `${window.location.pathname}${window.location.search}`;

  const choose = (change: () => void) => {
    change();
    setStartsAt('');
    setError(undefined);
  };

  const book = async (event: FormEvent) => {
    event.preventDefault();
    if (!service || !chosen) return;
    setBooking(true);
    setErrors({});
    setError(undefined);
    try {
      const response = await apiFetch(`/api/public/businesses/${encodeURIComponent(slug)}/bookings`, {
        method: 'POST',
        body: JSON.stringify({
          serviceId: service.id,
          staffMemberId: staffId || null,
          startsAt: chosen.startsAt,
          clientName,
          clientEmail,
        }),
      });
      if (response.ok) {
        setBooked((await response.json()) as Confirmation);
        setStartsAt('');
      } else {
        const problem = await readProblem(response);
        if (problem.errors) setErrors(problem.errors);
        else setError(problem.title ?? `HTTP error! status: ${response.status}`);
        // Someone else took it: show what's still free (MVP-4).
        if (response.status === 409) {
          setStartsAt('');
          await loadSlots();
        }
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to call the API');
    }
    setBooking(false);
  };

  if (booked) {
    return (
      <section className="card" aria-labelledby="booked-heading">
        <h2 id="booked-heading" className="section-title">You're booked</h2>
        <p>
          {booked.serviceName} with {booked.staffName}, {dayLabel(booked.date)} {booked.start}–{booked.end}.
        </p>
        <div className="form-actions">
          <a className="button" href="/my-bookings">
            My bookings
          </a>
          <button
            type="button"
            className="button button-secondary"
            onClick={() => {
              setBooked(undefined);
              void loadSlots();
            }}
          >
            Book another time
          </button>
        </div>
      </section>
    );
  }

  return (
    <>
      <section className="card" aria-labelledby="business-heading">
        {/* V1-6: so clients know they're in the right place. */}
        <div className="business-intro">
          {business.logoUrl && <img className="business-logo" src={business.logoUrl} alt="" />}
          <div>
            <h2 id="business-heading" className="section-title">{business.name}</h2>
            {business.address && <p className="business-address">{business.address}</p>}
          </div>
        </div>
        {business.description && <p className="business-description">{business.description}</p>}
        {mine?.slug === business.slug && (
          <p className="hint">
            This is your business. <a href="/business-page">Edit the logo, address and description</a>
          </p>
        )}
        {business.services.length === 0 ? (
          <p className="hint">Nothing can be booked here yet.</p>
        ) : (
          <fieldset className="field choice-group">
            <legend>Service</legend>
            <div className="option-cards">
              {business.services.map((option) => (
                <label key={option.id} className={`option-card ${option.id === serviceId ? 'selected' : ''}`}>
                  <input
                    type="radio"
                    name="service"
                    value={option.id}
                    checked={option.id === serviceId}
                    onChange={() => choose(() => {
                      setServiceId(option.id);
                      setStaffId('');
                    })}
                  />
                  <span className="service-name">{option.name}</span>
                  <span className="hint">{duration(option.durationMinutes)} · {price(option)}</span>
                </label>
              ))}
            </div>
          </fieldset>
        )}

        {service && (
          <div className="field">
            <label htmlFor="staff">With</label>
            <select
              id="staff"
              className="input input-narrow"
              value={staffId}
              onChange={(event) => choose(() => setStaffId(event.target.value))}
            >
              <option value="">Anyone</option>
              {service.staff.map((member) => (
                <option key={member.id} value={member.id}>{member.name}</option>
              ))}
            </select>
          </div>
        )}
      </section>

      {service && (
        <section className="card" aria-labelledby="times-heading">
          <div className="calendar-header">
            <h2 id="times-heading" className="section-title">Free times</h2>
            <div className="toggle" role="group" aria-label="Weeks">
              <button type="button" className="button button-secondary" disabled={week === 0} onClick={() => setWeek(week - 1)}>
                <span aria-hidden="true">‹</span> Earlier
              </button>
              <button
                type="button"
                className="button button-secondary"
                disabled={week === weeks - 1}
                onClick={() => setWeek(week + 1)}
              >
                Later <span aria-hidden="true">›</span>
              </button>
            </div>
          </div>
          <p className="hint">
            {dayLabel(shownDays[0])} – {dayLabel(shownDays[6])}, times in {business.timeZone.replace(/_/g, ' ')}
          </p>

          {!days ? (
            <p className="status" role="status">Loading...</p>
          ) : (
            <div className="slot-days" aria-busy={loadingSlots}>
              {shownDays.map((day) => (
                <div key={day} className="slot-day">
                  <h3 className="calendar-day-name">{dayLabel(day)}</h3>
                  {slotsOn(day).length === 0 ? (
                    <span className="hint">No free times</span>
                  ) : (
                    <div className="slot-times">
                      {slotsOn(day).map((slot) => (
                        <button
                          key={slot.startsAt}
                          type="button"
                          className={`button button-secondary slot ${slot.startsAt === startsAt ? 'active' : ''}`}
                          aria-pressed={slot.startsAt === startsAt}
                          disabled={loadingSlots}
                          onClick={() => {
                            setStartsAt(slot.startsAt);
                            setError(undefined);
                          }}
                        >
                          {slot.start}
                        </button>
                      ))}
                    </div>
                  )}
                </div>
              ))}
            </div>
          )}
          {error && <ErrorMessage message={error} />}
        </section>
      )}

      {service && chosen && (
        <section className="card" aria-labelledby="summary-heading">
          <h2 id="summary-heading" className="section-title">Your booking</h2>
          <p>
            {service.name} with {staffMember?.name ?? 'anyone free'}, {dayLabel(chosen.date)}{' '}
            {chosen.start}–{addMinutes(chosen.start, service.durationMinutes)} · {price(service)}
          </p>
          {/* V1-3: clients see the policy before they book. */}
          <p className="hint">{describePolicy(business.cancellationNoticeHours)}</p>

          {!signInEnabled ? (
            <p className="hint">Booking needs sign-in, which isn't set up yet.</p>
          ) : !user ? (
            // MVP-3: sign in or sign up, then come back to this same slot.
            <div className="form-actions">
              <a className="button" href={signInUrl(returnUrl)}>Sign in to book</a>
              <a className="button button-secondary" href={signInUrl(returnUrl, true)}>Create an account</a>
            </div>
          ) : (
            <form className="form" onSubmit={(event) => void book(event)} noValidate>
              <div className="field">
                <label htmlFor="client-name">Your name</label>
                <input
                  id="client-name"
                  className="input"
                  value={clientName}
                  maxLength={100}
                  autoComplete="name"
                  aria-invalid={Boolean(errors.clientName)}
                  onChange={(event) => setClientName(event.target.value)}
                />
                {errors.clientName && <p className="field-error">{errors.clientName[0]}</p>}
              </div>
              <div className="field">
                <label htmlFor="client-email">Your email</label>
                <input
                  id="client-email"
                  className="input"
                  type="email"
                  value={clientEmail}
                  maxLength={254}
                  autoComplete="email"
                  aria-invalid={Boolean(errors.clientEmail)}
                  onChange={(event) => setClientEmail(event.target.value)}
                />
                {errors.clientEmail && <p className="field-error">{errors.clientEmail[0]}</p>}
              </div>
              <div className="form-actions">
                <button className="button" type="submit" disabled={booking}>
                  {booking ? 'Booking...' : 'Book'}
                </button>
              </div>
            </form>
          )}
        </section>
      )}
    </>
  );
}
