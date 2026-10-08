import { useEffect, useState, type FormEvent } from 'react';
import { apiFetch, readProblem } from './api.ts';
import { SlotPicker } from './slot-picker.tsx';
import { ErrorMessage } from './ui.tsx';

type Service = { id: string; name: string; staff: { id: string; name: string }[] };

export type AddedBooking = { serviceName: string; staffName: string; date: string; start: string; end: string };

// User story V1-5: the owner books someone who phoned, by name and email, at a free time from
// the booking page's own list. The client gets the confirmation email.
export function AddBooking({ onBooked, onClose }: { onBooked: (booked: AddedBooking, clientName: string) => void; onClose: () => void }) {
  const [slug, setSlug] = useState<string>();
  const [services, setServices] = useState<Service[]>();
  const [serviceId, setServiceId] = useState('');
  const [staffMemberId, setStaffMemberId] = useState('');
  const [startsAt, setStartsAt] = useState<string>();
  const [clientName, setClientName] = useState('');
  const [clientEmail, setClientEmail] = useState('');
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [error, setError] = useState<string>();
  const [booking, setBooking] = useState(false);

  // The services and who does them, as clients see them on the booking page.
  useEffect(() => {
    let current = true;
    (async () => {
      const mine = await apiFetch('/api/businesses/mine');
      if (!mine.ok) throw new Error(`HTTP error! status: ${mine.status}`);
      const { slug } = (await mine.json()) as { slug: string };
      const response = await apiFetch(`/api/public/businesses/${encodeURIComponent(slug)}`);
      if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);
      const found = ((await response.json()) as { services: Service[] }).services;
      if (!current) return;
      setSlug(slug);
      setServices(found);
      setServiceId(found[0]?.id ?? '');
    })().catch((err) => current && setError(err instanceof Error ? err.message : 'Failed to call the API'));
    return () => {
      current = false;
    };
  }, []);

  const service = services?.find((candidate) => candidate.id === serviceId);
  const slotsQuery = new URLSearchParams({ serviceId });
  if (staffMemberId) slotsQuery.set('staffMemberId', staffMemberId);

  const book = async (event: FormEvent) => {
    event.preventDefault();
    setBooking(true);
    setError(undefined);
    setErrors({});
    try {
      const response = await apiFetch('/api/businesses/mine/bookings', {
        method: 'POST',
        body: JSON.stringify({ serviceId, staffMemberId: staffMemberId || null, startsAt, clientName, clientEmail }),
      });
      if (!response.ok) {
        const problem = await readProblem(response);
        if (problem.errors) setErrors(problem.errors);
        else setError(problem.title ?? `HTTP error! status: ${response.status}`);
        // The time was just taken: the list without it lets the owner choose again.
        if (response.status === 409) setStartsAt(undefined);
      } else {
        onBooked((await response.json()) as AddedBooking, clientName.trim());
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to call the API');
    }
    setBooking(false);
  };

  return (
    <section className="card move-booking" aria-labelledby="add-booking-heading">
      <div className="calendar-header">
        <h3 id="add-booking-heading" className="section-title">Add a booking</h3>
        <button type="button" className="button button-secondary" onClick={onClose}>
          Close
        </button>
      </div>
      {error && <ErrorMessage message={error} />}
      {!services ? (
        !error && <p className="status" role="status">Loading...</p>
      ) : services.length === 0 ? (
        <p className="hint">Add a service first: there's nothing to book yet.</p>
      ) : (
        <form onSubmit={(event) => void book(event)}>
          <div className="field">
            <label htmlFor="add-booking-service">Service</label>
            <select
              id="add-booking-service"
              className="input input-narrow"
              value={serviceId}
              onChange={(event) => {
                setServiceId(event.target.value);
                setStaffMemberId('');
                setStartsAt(undefined);
              }}
            >
              {services.map((option) => (
                <option key={option.id} value={option.id}>{option.name}</option>
              ))}
            </select>
          </div>
          {service && (
            <div className="field">
              <label htmlFor="add-booking-staff">With</label>
              <select
                id="add-booking-staff"
                className="input input-narrow"
                value={staffMemberId}
                onChange={(event) => {
                  setStaffMemberId(event.target.value);
                  setStartsAt(undefined);
                }}
              >
                <option value="">Anyone</option>
                {service.staff.map((member) => (
                  <option key={member.id} value={member.id}>{member.name}</option>
                ))}
              </select>
            </div>
          )}
          {slug && serviceId && (
            <SlotPicker
              url={`/api/public/businesses/${encodeURIComponent(slug)}/slots?${slotsQuery}`}
              startsAt={startsAt}
              onChoose={setStartsAt}
            />
          )}
          {errors.startsAt && <p className="field-error">{errors.startsAt[0]}</p>}
          <div className="field">
            <label htmlFor="add-booking-name">Client's name</label>
            <input
              id="add-booking-name"
              className="input"
              value={clientName}
              onChange={(event) => setClientName(event.target.value)}
              autoComplete="off"
              aria-invalid={Boolean(errors.clientName)}
              aria-describedby={errors.clientName ? 'add-booking-name-error' : undefined}
            />
            {errors.clientName && <p id="add-booking-name-error" className="field-error">{errors.clientName[0]}</p>}
          </div>
          <div className="field">
            <label htmlFor="add-booking-email">Client's email</label>
            <input
              id="add-booking-email"
              className="input"
              type="email"
              value={clientEmail}
              onChange={(event) => setClientEmail(event.target.value)}
              autoComplete="off"
              aria-invalid={Boolean(errors.clientEmail)}
              aria-describedby="add-booking-email-hint"
            />
            <p id="add-booking-email-hint" className={errors.clientEmail ? 'field-error' : 'field-hint'}>
              {errors.clientEmail?.[0] ?? 'They get the confirmation here; no account needed.'}
            </p>
          </div>
          <div className="form-actions">
            <button
              type="submit"
              className="button"
              disabled={!startsAt || !clientName.trim() || !clientEmail.trim() || booking}
            >
              {booking ? 'Booking...' : 'Book'}
            </button>
          </div>
        </form>
      )}
    </section>
  );
}
