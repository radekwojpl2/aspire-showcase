import { useEffect, useState } from 'react';
import { apiFetch, readProblem } from './api.ts';
import { ErrorMessage } from './ui.tsx';

type Slot = { start: string; startsAt: string };
type Day = { date: string; slots: Slot[] };
export type MovedBooking = { id: string; serviceName: string; staffName: string; date: string; start: string; end: string };

const dayLabel = (day: string) =>
  new Date(`${day}T00:00:00Z`).toLocaleDateString(undefined, {
    weekday: 'short',
    day: 'numeric',
    month: 'short',
    timeZone: 'UTC',
  });

// How many days with free times show at first, and with each "More days".
const daysShown = 5;

// User story V1-4: picks a free time for a booking and moves it there. The client's and the
// owner's endpoints work the same way; only the owner may choose the staff member.
export function MoveBooking({
  what,
  basePath,
  staff,
  onMoved,
  onClose,
}: {
  // What is being moved, such as "Haircut on Mon 2 Nov, 09:00".
  what: string;
  // The booking under /api/me/bookings or /api/businesses/mine/bookings.
  basePath: string;
  // Who it can move to; left out, whoever is free (the same person first).
  staff?: { id: string; name: string }[];
  onMoved: (moved: MovedBooking) => void;
  onClose: () => void;
}) {
  const [staffMemberId, setStaffMemberId] = useState('');
  const [days, setDays] = useState<Day[]>();
  const [shown, setShown] = useState(daysShown);
  const [startsAt, setStartsAt] = useState<string>();
  const [timeZone, setTimeZone] = useState('');
  const [error, setError] = useState<string>();
  const [moving, setMoving] = useState(false);

  useEffect(() => {
    let current = true;
    setDays(undefined);
    setStartsAt(undefined);
    const query = staffMemberId ? `?${new URLSearchParams({ staffMemberId })}` : '';
    apiFetch(`${basePath}/slots${query}`)
      .then(async (response) => {
        if (!response.ok) throw new Error((await readProblem(response)).title ?? `HTTP error! status: ${response.status}`);
        const body = (await response.json()) as { timeZone: string; days: Day[] };
        if (!current) return;
        setTimeZone(body.timeZone);
        setDays(body.days);
      })
      .catch((err) => current && setError(err instanceof Error ? err.message : 'Failed to call the API'));
    return () => {
      current = false;
    };
  }, [basePath, staffMemberId]);

  const move = async () => {
    setMoving(true);
    setError(undefined);
    try {
      const response = await apiFetch(`${basePath}/reschedule`, {
        method: 'POST',
        body: JSON.stringify({ startsAt, staffMemberId: staffMemberId || null }),
      });
      if (!response.ok) throw new Error((await readProblem(response)).title ?? `HTTP error! status: ${response.status}`);
      onMoved((await response.json()) as MovedBooking);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to call the API');
    }
    setMoving(false);
  };

  return (
    <section className="card move-booking" aria-labelledby="move-heading">
      <div className="calendar-header">
        <h3 id="move-heading" className="section-title">Move {what}</h3>
        <button type="button" className="button button-secondary" onClick={onClose}>
          Close
        </button>
      </div>
      {staff && (
        <select
          className="input input-narrow"
          aria-label="With"
          value={staffMemberId}
          onChange={(event) => setStaffMemberId(event.target.value)}
        >
          <option value="">Whoever is free</option>
          {staff.map((member) => (
            <option key={member.id} value={member.id}>{member.name}</option>
          ))}
        </select>
      )}
      {error && <ErrorMessage message={error} />}
      {!days ? (
        !error && <p className="status" role="status">Loading...</p>
      ) : days.length === 0 ? (
        <p className="hint">No free times in the next 4 weeks.</p>
      ) : (
        <>
          <p className="hint">Times in {timeZone.replace(/_/g, ' ')}</p>
          <div className="slot-days">
            {days.slice(0, shown).map((day) => (
              <div key={day.date} className="slot-day">
                <h4 className="calendar-day-name">{dayLabel(day.date)}</h4>
                <div className="slot-times">
                  {day.slots.map((slot) => (
                    <button
                      key={slot.startsAt}
                      type="button"
                      className={`button button-secondary slot ${slot.startsAt === startsAt ? 'active' : ''}`}
                      aria-pressed={slot.startsAt === startsAt}
                      onClick={() => setStartsAt(slot.startsAt)}
                    >
                      {slot.start}
                    </button>
                  ))}
                </div>
              </div>
            ))}
          </div>
          <div className="form-actions">
            {shown < days.length && (
              <button type="button" className="button button-secondary" onClick={() => setShown(shown + daysShown)}>
                More days
              </button>
            )}
            <button type="button" className="button" disabled={!startsAt || moving} onClick={() => void move()}>
              {moving ? 'Moving...' : 'Move here'}
            </button>
          </div>
        </>
      )}
    </section>
  );
}
