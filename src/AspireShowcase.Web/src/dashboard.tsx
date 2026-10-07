import { useEffect, useState } from 'react';
import { apiFetch } from './api.ts';
import { BookingsPage } from './bookings.tsx';
import { cancelBooking, confirmCancel, fetchCalendar, type Calendar, type CalendarBooking } from './calendar-api.ts';
import { OpeningHoursPage } from './hours.tsx';
import { ServicesPage } from './services.tsx';
import { StaffPage } from './staff.tsx';
import { TimeOffPage } from './time-off.tsx';
import { ErrorMessage } from './ui.tsx';

export type OwnedBusiness = { name: string; slug: string };

// Each tab has its own address, so links, bookmarks and the back button work, but switching
// tabs doesn't reload the page.
const tabs = [
  { path: '/', label: 'Today' },
  { path: '/bookings', label: 'Bookings' },
  { path: '/services', label: 'Services' },
  { path: '/staff', label: 'Staff' },
  { path: '/hours', label: 'Opening hours' },
  { path: '/time-off', label: 'Time off' },
] as const;

type Tab = (typeof tabs)[number]['path'];

const tabFromAddress = (): Tab => tabs.find((tab) => tab.path === window.location.pathname)?.path ?? '/';

// The owner's home: everything about the business on one page, a tab apart.
export function OwnerDashboard({ business }: { business: OwnedBusiness }) {
  const [tab, setTab] = useState<Tab>(tabFromAddress);

  useEffect(() => {
    const onBack = () => setTab(tabFromAddress());
    window.addEventListener('popstate', onBack);
    return () => window.removeEventListener('popstate', onBack);
  }, []);

  const go = (path: Tab) => {
    if (path === tab) return;
    window.history.pushState(null, '', path);
    setTab(path);
  };

  return (
    <div className="dashboard">
      <div className="dashboard-header">
        <h2 className="section-title">{business.name}</h2>
        <nav className="tabs" aria-label="Your business">
          {tabs.map(({ path, label }) => (
            <a
              key={path}
              href={path}
              className={`tab ${path === tab ? 'active' : ''}`}
              aria-current={path === tab ? 'page' : undefined}
              onClick={(event) => {
                // Plain clicks switch in place; ctrl- or middle-click still open a new tab.
                if (event.ctrlKey || event.metaKey || event.shiftKey || event.button !== 0) return;
                event.preventDefault();
                go(path);
              }}
            >
              {label}
            </a>
          ))}
        </nav>
      </div>

      {tab === '/' && <Today business={business} go={go} />}
      {tab === '/bookings' && <BookingsPage />}
      {tab === '/services' && <ServicesPage />}
      {tab === '/staff' && <StaffPage />}
      {tab === '/hours' && <OpeningHoursPage />}
      {tab === '/time-off' && <TimeOffPage />}
    </div>
  );
}

type Overview = {
  today: Calendar;
  week: Calendar;
  hasHours: boolean;
  hasServices: boolean;
};

// "2026-10-06 14:05" in the business's time zone, to compare with bookings' local times.
const nowIn = (timeZone: string) =>
  new Intl.DateTimeFormat('sv-SE', {
    timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  }).format(new Date());

const dayLabel = (day: string, options: Intl.DateTimeFormatOptions) =>
  new Date(`${day}T00:00:00Z`).toLocaleDateString(undefined, { ...options, timeZone: 'UTC' });

// What an owner checks first: today's bookings, the week, the booking link, and what's left to set up.
function Today({ business, go }: { business: OwnedBusiness; go: (tab: Tab) => void }) {
  const [overview, setOverview] = useState<Overview | null>();
  const [error, setError] = useState<string>();
  const [reload, setReload] = useState(0);
  const [cancelling, setCancelling] = useState<string>();
  const [status, setStatus] = useState<string>();

  useEffect(() => {
    let current = true;
    const get = (path: string) => apiFetch(`/api/businesses/mine/${path}`).then((response) => (response.ok ? response.json() : null));
    Promise.all([fetchCalendar('day'), fetchCalendar('week'), get('services'), get('opening-hours')])
      .then(([today, week, services, hours]) => {
        if (!current) return;
        setOverview(
          today && week
            ? {
                today,
                week,
                hasServices: Array.isArray(services) && services.length > 0,
                hasHours: Boolean(hours?.periods?.length),
              }
            : null,
        );
      })
      .catch((err) => current && setError(err instanceof Error ? err.message : 'Failed to call the API'));
    return () => {
      current = false;
    };
  }, [reload]);

  if (error) return <ErrorMessage message={error} />;
  if (overview === undefined) return <p className="status" role="status">Loading...</p>;
  if (overview === null) {
    // Signed in before the business existed: the token has no owner role yet.
    return (
      <section className="card">
        <p className="hint">Sign out and in again to manage your business.</p>
      </section>
    );
  }

  const { today, week, hasHours, hasServices } = overview;
  const now = nowIn(today.timeZone);
  const upcoming = week.bookings.filter((booking) => `${booking.day} ${booking.start}` > now);
  const next = upcoming[0];
  const link = `${window.location.origin}/book/${business.slug}`;

  const cancel = async (booking: CalendarBooking) => {
    if (!confirmCancel(booking)) return;
    setCancelling(booking.id);
    setStatus(undefined);
    try {
      await cancelBooking(booking);
      setStatus(`${booking.clientName}'s booking at ${booking.start} is cancelled; they get an email.`);
      setReload((count) => count + 1);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to call the API');
    }
    setCancelling(undefined);
  };

  const copy = async () => {
    await navigator.clipboard.writeText(link);
    setStatus('Booking link copied.');
  };

  return (
    <>
      {(!hasHours || !hasServices) && (
        <section className="card setup" aria-labelledby="setup-heading">
          <h3 id="setup-heading" className="section-title">Before clients can book</h3>
          <ul className="setup-steps">
            {!hasHours && (
              <li>
                <span>Set the hours you're open.</span>
                <button type="button" className="button" onClick={() => go('/hours')}>Set opening hours</button>
              </li>
            )}
            {!hasServices && (
              <li>
                <span>Add what clients can book.</span>
                <button type="button" className="button" onClick={() => go('/services')}>Add services</button>
              </li>
            )}
          </ul>
        </section>
      )}

      <div className="stats">
        <button type="button" className="stat" onClick={() => go('/bookings')}>
          <span className="stat-value">{today.bookings.length}</span>
          <span className="stat-label">today</span>
        </button>
        <button type="button" className="stat" onClick={() => go('/bookings')}>
          <span className="stat-value">{week.bookings.length}</span>
          <span className="stat-label">this week</span>
        </button>
        <div className="stat stat-wide">
          <span className="stat-label">Next</span>
          <span className="stat-next">
            {next
              ? `${dayLabel(next.day, { weekday: 'short', day: 'numeric', month: 'short' })}, ${next.start} · ${next.serviceName}, ${next.clientName}`
              : 'Nothing booked for the rest of this week'}
          </span>
        </div>
      </div>

      <section className="card" aria-labelledby="today-heading">
        <div className="calendar-header">
          <h3 id="today-heading" className="section-title">
            Today, {dayLabel(today.firstDay, { weekday: 'long', day: 'numeric', month: 'long' })}
          </h3>
          <button type="button" className="button button-secondary" onClick={() => go('/bookings')}>
            Whole week
          </button>
        </div>
        <p className="field-hint" role="status">{status ?? ''}</p>
        {today.bookings.length === 0 ? (
          <p className="hint">No bookings today.</p>
        ) : (
          <ul className="agenda">
            {today.bookings.map((booking) => {
              const past = `${booking.day} ${booking.end}` <= now;
              return (
                <li key={booking.id} className={`agenda-item ${past ? 'past' : ''}`}>
                  <span className="agenda-time">{booking.start}–{booking.end}</span>
                  <span className="agenda-what">
                    <strong>{booking.clientName}</strong> · {booking.serviceName} with {booking.staffName}
                    <a className="calendar-email" href={`mailto:${booking.clientEmail}`}>{booking.clientEmail}</a>
                  </span>
                  {!past && (
                    <button
                      type="button"
                      className="button button-secondary calendar-cancel"
                      disabled={cancelling === booking.id}
                      onClick={() => void cancel(booking)}
                    >
                      {cancelling === booking.id ? 'Cancelling...' : 'Cancel'}
                      <span className="visually-hidden"> {booking.clientName}, {booking.start}</span>
                    </button>
                  )}
                </li>
              );
            })}
          </ul>
        )}
      </section>

      <section className="card" aria-labelledby="link-heading">
        <h3 id="link-heading" className="section-title">Your booking link</h3>
        <div className="link-row">
          <span className="booking-link">{link}</span>
          <button type="button" className="button button-secondary" onClick={() => void copy()}>Copy</button>
          <a className="button button-secondary" href={`/book/${business.slug}`} target="_blank" rel="noopener noreferrer">
            Open<span className="visually-hidden"> (opens in new tab)</span>
          </a>
        </div>
      </section>
    </>
  );
}
