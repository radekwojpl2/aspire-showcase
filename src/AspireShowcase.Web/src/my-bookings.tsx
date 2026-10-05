import { useCallback, useEffect, useState } from 'react';
import { apiFetch, readProblem } from './api.ts';
import { signInUrl, useSession } from './session.ts';
import { ErrorMessage } from './ui.tsx';

// Dates and times are in each business's own time zone.
type ClientBooking = {
  id: string;
  businessName: string;
  businessSlug: string;
  serviceName: string;
  staffName: string;
  date: string;
  start: string;
  end: string;
  timeZone: string;
};

const dayLabel = (day: string) =>
  new Date(`${day}T00:00:00Z`).toLocaleDateString(undefined, {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    timeZone: 'UTC',
  });

// User story MVP-7: a client's upcoming bookings at every business, and cancelling one.
export function MyBookingsPage() {
  const { signInEnabled, user } = useSession();
  const [bookings, setBookings] = useState<ClientBooking[]>();
  const [error, setError] = useState<string>();
  const [cancelling, setCancelling] = useState<string>();
  const [cancelled, setCancelled] = useState<string>();

  useEffect(() => {
    if (signInEnabled && !user) window.location.replace(signInUrl('/my-bookings'));
  }, [signInEnabled, user]);

  const load = useCallback(async () => {
    const response = await apiFetch('/api/me/bookings');
    if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);
    setBookings((await response.json()) as ClientBooking[]);
  }, []);

  useEffect(() => {
    if (!user) return;
    load().catch((err) => setError(err instanceof Error ? err.message : 'Failed to call the API'));
  }, [user, load]);

  if (!signInEnabled) {
    return (
      <section className="card">
        <p className="hint">Sign-in isn't set up yet, so there are no bookings to show.</p>
      </section>
    );
  }
  if (!user) return <p className="status" role="status">Taking you to sign-in...</p>;
  if (!bookings) return error ? <ErrorMessage message={error} /> : <p className="status" role="status">Loading...</p>;

  const cancel = async (booking: ClientBooking) => {
    if (!window.confirm(`Cancel ${booking.serviceName} at ${booking.businessName} on ${dayLabel(booking.date)}, ${booking.start}?`)) {
      return;
    }
    setCancelling(booking.id);
    setError(undefined);
    setCancelled(undefined);
    try {
      const response = await apiFetch(`/api/me/bookings/${booking.id}/cancel`, { method: 'POST' });
      if (!response.ok) throw new Error((await readProblem(response)).title ?? `HTTP error! status: ${response.status}`);
      setCancelled(`${booking.serviceName} on ${dayLabel(booking.date)} is cancelled, and the time is free again.`);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to call the API');
    }
    setCancelling(undefined);
  };

  return (
    <section className="card" aria-labelledby="my-bookings-heading">
      <h2 id="my-bookings-heading" className="section-title">My bookings</h2>
      <p className="field-hint" role="status">{cancelled ?? ''}</p>
      {error && <ErrorMessage message={error} />}
      {bookings.length === 0 ? (
        <p className="hint">You have no upcoming bookings.</p>
      ) : (
        <ul className="service-list">
          {bookings.map((booking) => (
            <li key={booking.id} className="service-item">
              <div className="service-summary">
                <span className="service-name">
                  {dayLabel(booking.date)}, {booking.start}–{booking.end}
                </span>
                <span>
                  {booking.serviceName} with {booking.staffName}
                </span>
                <span className="hint">
                  <a href={`/book/${booking.businessSlug}`}>{booking.businessName}</a> · times in{' '}
                  {booking.timeZone.replace(/_/g, ' ')}
                </span>
              </div>
              <div className="service-actions">
                <button
                  type="button"
                  className="button button-secondary"
                  disabled={cancelling === booking.id}
                  onClick={() => void cancel(booking)}
                >
                  {cancelling === booking.id ? 'Cancelling...' : 'Cancel'}
                  <span className="visually-hidden"> {booking.serviceName} on {dayLabel(booking.date)}</span>
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
