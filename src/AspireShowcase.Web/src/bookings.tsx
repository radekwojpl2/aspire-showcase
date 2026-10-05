import { useEffect, useState } from 'react';
import { apiFetch, readProblem } from './api.ts';
import { signInUrl, useSession } from './session.ts';
import { ErrorMessage } from './ui.tsx';

type View = 'day' | 'week';

// Days and times are already in the business's time zone: "2026-10-06", "09:00".
type CalendarBooking = {
  id: string;
  day: string;
  start: string;
  end: string;
  staffMemberId: string;
  staffName: string;
  serviceName: string;
  clientName: string;
  clientEmail: string;
};

type Calendar = {
  view: View;
  date: string;
  firstDay: string;
  lastDay: string;
  timeZone: string;
  staff: { id: string; name: string }[];
  bookings: CalendarBooking[];
};

type Load = 'loading' | 'loaded' | 'no-business' | 'not-owner';

// Calendar dates are plain dates, so the arithmetic is done in UTC, where no day is skipped.
const parse = (day: string) => new Date(`${day}T00:00:00Z`);
const format = (date: Date) => date.toISOString().slice(0, 10);
const addDays = (day: string, days: number) => {
  const date = parse(day);
  date.setUTCDate(date.getUTCDate() + days);
  return format(date);
};
const label = (day: string, options: Intl.DateTimeFormatOptions) =>
  parse(day).toLocaleDateString(undefined, { ...options, timeZone: 'UTC' });

function daysBetween(first: string, last: string): string[] {
  const days = [];
  for (let day = first; day <= last; day = addDays(day, 1)) days.push(day);
  return days;
}

// User story MVP-12: the owner sees bookings by day or week, for everyone or one staff member.
export function BookingsPage() {
  const { signInEnabled, user } = useSession();
  const [load, setLoad] = useState<Load>('loading');
  const [error, setError] = useState<string>();
  const [calendar, setCalendar] = useState<Calendar>();
  const [view, setView] = useState<View>('week');
  // Undefined until the first answer: the API knows "today" in the business's time zone.
  const [date, setDate] = useState<string>();
  const [staffMemberId, setStaffMemberId] = useState('');
  // Bumped to load the calendar again, after a cancellation.
  const [reload, setReload] = useState(0);
  const [cancelling, setCancelling] = useState<string>();
  const [cancelled, setCancelled] = useState<string>();

  useEffect(() => {
    if (signInEnabled && !user) window.location.replace(signInUrl('/bookings'));
  }, [signInEnabled, user]);

  useEffect(() => {
    if (!user) return;
    let current = true;
    const query = new URLSearchParams({ view });
    if (date) query.set('date', date);
    if (staffMemberId) query.set('staffMemberId', staffMemberId);
    apiFetch(`/api/businesses/mine/bookings?${query}`)
      .then(async (response) => {
        if (!current) return;
        if (response.status === 404) return setLoad('no-business');
        if (response.status === 403) return setLoad('not-owner');
        if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);
        const body = (await response.json()) as Calendar;
        setCalendar(body);
        setError(undefined);
        setLoad('loaded');
      })
      .catch((err) => current && setError(err instanceof Error ? err.message : 'Failed to call the API'));
    return () => {
      current = false;
    };
  }, [user, view, date, staffMemberId, reload]);

  if (!signInEnabled) {
    return (
      <section className="card">
        <p className="hint">Sign-in isn't set up yet, so there are no bookings to show.</p>
      </section>
    );
  }
  if (!user) return <p className="status" role="status">Taking you to sign-in...</p>;
  if (load === 'no-business' || load === 'not-owner') {
    return (
      <section className="card">
        <p className="hint">
          {load === 'no-business' ? "You haven't started a business yet." : 'Only owners can see bookings.'}{' '}
          <a href="/start">Start your business</a>
        </p>
      </section>
    );
  }
  if (!calendar) return error ? <ErrorMessage message={error} /> : <p className="status" role="status">Loading...</p>;

  // User story MVP-14: the owner cancels for sickness or emergencies; the time is free again.
  const cancel = async (booking: CalendarBooking) => {
    if (!window.confirm(`Cancel ${booking.clientName}'s ${booking.serviceName} on ${booking.day}, ${booking.start}?`)) return;
    setCancelling(booking.id);
    setError(undefined);
    setCancelled(undefined);
    try {
      const response = await apiFetch(`/api/businesses/mine/bookings/${booking.id}/cancel`, { method: 'POST' });
      if (!response.ok) throw new Error((await readProblem(response)).title ?? `HTTP error! status: ${response.status}`);
      setCancelled(`${booking.clientName}'s booking at ${booking.start} is cancelled, and the time is free again.`);
      setReload((count) => count + 1);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to call the API');
    }
    setCancelling(undefined);
  };

  const step = view === 'week' ? 7 : 1;
  const days = daysBetween(calendar.firstDay, calendar.lastDay);
  const title =
    view === 'day'
      ? label(calendar.firstDay, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })
      : `${label(calendar.firstDay, { day: 'numeric', month: 'short' })} – ${label(calendar.lastDay, {
          day: 'numeric',
          month: 'short',
          year: 'numeric',
        })}`;

  return (
    <section className="card calendar-card" aria-labelledby="bookings-heading">
      <div className="calendar-header">
        <h2 id="bookings-heading" className="section-title">Bookings</h2>
        <div className="calendar-toolbar">
          <div className="toggle" role="group" aria-label="View">
            {(['day', 'week'] as const).map((option) => (
              <button
                key={option}
                type="button"
                className={`button button-secondary ${view === option ? 'active' : ''}`}
                aria-pressed={view === option}
                onClick={() => setView(option)}
              >
                {option === 'day' ? 'Day' : 'Week'}
              </button>
            ))}
          </div>
          <div className="toggle" role="group" aria-label="Dates">
            <button type="button" className="button button-secondary" onClick={() => setDate(addDays(calendar.date, -step))}>
              <span aria-hidden="true">‹</span> Previous
            </button>
            <button type="button" className="button button-secondary" onClick={() => setDate(undefined)}>
              Today
            </button>
            <button type="button" className="button button-secondary" onClick={() => setDate(addDays(calendar.date, step))}>
              Next <span aria-hidden="true">›</span>
            </button>
          </div>
          <select
            className="input input-narrow"
            aria-label="Staff"
            value={staffMemberId}
            onChange={(event) => setStaffMemberId(event.target.value)}
          >
            <option value="">Everyone</option>
            {calendar.staff.map((member) => (
              <option key={member.id} value={member.id}>{member.name}</option>
            ))}
          </select>
        </div>
      </div>

      <p className="calendar-title" aria-live="polite">
        {title} <span className="hint">({calendar.timeZone.replace(/_/g, ' ')})</span>
      </p>
      <p className="field-hint" role="status">{cancelled ?? ''}</p>
      {error && <ErrorMessage message={error} />}

      <div className={`calendar calendar-${view}`}>
        {days.map((day) => {
          const bookings = calendar.bookings.filter((booking) => booking.day === day);
          return (
            <section key={day} className="calendar-day" aria-label={label(day, { weekday: 'long', day: 'numeric', month: 'long' })}>
              {view === 'week' && (
                <h3 className="calendar-day-name">{label(day, { weekday: 'short', day: 'numeric', month: 'short' })}</h3>
              )}
              {bookings.length === 0 ? (
                <p className="hint">No bookings</p>
              ) : (
                <ul className="calendar-bookings">
                  {bookings.map((booking) => (
                    <li key={booking.id} className="calendar-booking">
                      <span className="calendar-time">{booking.start}–{booking.end}</span>
                      <span className="calendar-client">{booking.clientName}</span>
                      <span className="hint">{booking.serviceName} · {booking.staffName}</span>
                      <a className="calendar-email" href={`mailto:${booking.clientEmail}`}>{booking.clientEmail}</a>
                      <button
                        type="button"
                        className="button button-secondary calendar-cancel"
                        disabled={cancelling === booking.id}
                        onClick={() => void cancel(booking)}
                      >
                        {cancelling === booking.id ? 'Cancelling...' : 'Cancel'}
                        <span className="visually-hidden"> {booking.clientName}, {booking.start}</span>
                      </button>
                    </li>
                  ))}
                </ul>
              )}
            </section>
          );
        })}
      </div>
    </section>
  );
}
