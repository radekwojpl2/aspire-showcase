import { useState } from 'react';
import { apiFetch, readProblem } from './api.ts';
import { SlotPicker } from './slot-picker.tsx';
import { ErrorMessage } from './ui.tsx';

export type MovedBooking = { id: string; serviceName: string; staffName: string; date: string; start: string; end: string };

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
  const [startsAt, setStartsAt] = useState<string>();
  const [error, setError] = useState<string>();
  const [moving, setMoving] = useState(false);

  const query = staffMemberId ? `?${new URLSearchParams({ staffMemberId })}` : '';

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
          onChange={(event) => {
            setStaffMemberId(event.target.value);
            setStartsAt(undefined);
          }}
        >
          <option value="">Whoever is free</option>
          {staff.map((member) => (
            <option key={member.id} value={member.id}>{member.name}</option>
          ))}
        </select>
      )}
      {error && <ErrorMessage message={error} />}
      <SlotPicker url={`${basePath}/slots${query}`} startsAt={startsAt} onChoose={setStartsAt} />
      <div className="form-actions">
        <button type="button" className="button" disabled={!startsAt || moving} onClick={() => void move()}>
          {moving ? 'Moving...' : 'Move here'}
        </button>
      </div>
    </section>
  );
}
