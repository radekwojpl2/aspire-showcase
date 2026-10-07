import { useEffect, useState, type FormEvent } from 'react';
import { apiFetch, readProblem } from './api.ts';
import { cancelBooking, confirmCancel } from './calendar-api.ts';
import { MoveBooking } from './reschedule.tsx';
import { signInUrl, useSession } from './session.ts';
import { ErrorMessage } from './ui.tsx';

// Times are local to the business: "2026-11-02T09:00".
type TimeOffBooking = {
  id: string;
  day: string;
  start: string;
  end: string;
  staffName: string;
  serviceName: string;
  clientName: string;
};

type TimeOff = {
  id: string;
  staffMemberId: string | null;
  staffName: string | null;
  from: string;
  to: string;
  note: string | null;
  bookings: TimeOffBooking[];
};

type Staff = { id: string; name: string };

// What the form edits. A whole day is from its midnight to the next one's.
type Draft = {
  staffMemberId: string;
  allDay: boolean;
  fromDay: string;
  toDay: string;
  from: string;
  to: string;
  note: string;
};

type Load = 'loading' | 'loaded' | 'no-business' | 'not-owner';

// Dates are plain dates, so the arithmetic is done in UTC, where no day is skipped.
const nextDay = (day: string) => {
  const date = new Date(`${day}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + 1);
  return date.toISOString().slice(0, 10);
};
const dayLabel = (day: string) =>
  new Date(`${day}T00:00:00Z`).toLocaleDateString(undefined, {
    weekday: 'short',
    day: 'numeric',
    month: 'short',
    timeZone: 'UTC',
  });

// "Mon 2 Nov – Wed 4 Nov", or "Mon 2 Nov, 09:00–12:00" for part of a day.
function describe({ from, to }: Pick<TimeOff, 'from' | 'to'>): string {
  const [fromDay, fromTime] = from.split('T');
  const [toDay, toTime] = to.split('T');
  if (fromTime === '00:00' && toTime === '00:00') {
    const lastDay = new Date(`${toDay}T00:00:00Z`);
    lastDay.setUTCDate(lastDay.getUTCDate() - 1);
    const last = lastDay.toISOString().slice(0, 10);
    return last === fromDay ? `${dayLabel(fromDay)}, all day` : `${dayLabel(fromDay)} – ${dayLabel(last)}`;
  }
  return fromDay === toDay
    ? `${dayLabel(fromDay)}, ${fromTime}–${toTime}`
    : `${dayLabel(fromDay)} ${fromTime} – ${dayLabel(toDay)} ${toTime}`;
}

const today = () => new Date().toISOString().slice(0, 10);

const emptyDraft = (): Draft => ({
  staffMemberId: '',
  allDay: true,
  fromDay: today(),
  toDay: today(),
  from: `${today()}T12:00`,
  to: `${today()}T13:00`,
  note: '',
});

// User story V1-1: the owner blocks holidays and breaks, for the business or one staff member.
// Bookings already in a blocked time stay, listed here for the owner to cancel or move (V1-4).
export function TimeOffPage() {
  const { signInEnabled, user } = useSession();
  const [load, setLoad] = useState<Load>('loading');
  const [loadError, setLoadError] = useState<string>();
  const [timeOff, setTimeOff] = useState<TimeOff[]>([]);
  const [timeZone, setTimeZone] = useState('');
  const [staff, setStaff] = useState<Staff[]>([]);
  const [draft, setDraft] = useState<Draft>(emptyDraft);
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [formError, setFormError] = useState<string>();
  const [saving, setSaving] = useState(false);
  const [busy, setBusy] = useState<string>();
  const [status, setStatus] = useState('');
  const [moving, setMoving] = useState<TimeOffBooking>();
  // Bumped to load the list again, after a cancellation.
  const [reload, setReload] = useState(0);

  useEffect(() => {
    if (signInEnabled && !user) window.location.replace(signInUrl('/time-off'));
  }, [signInEnabled, user]);

  useEffect(() => {
    if (!user) return;
    let current = true;
    Promise.all([apiFetch('/api/businesses/mine/time-off'), apiFetch('/api/businesses/mine/staff')])
      .then(async ([list, people]) => {
        if (!current) return;
        if (list.status === 404) return setLoad('no-business');
        if (list.status === 403) return setLoad('not-owner');
        if (!list.ok || !people.ok) throw new Error(`HTTP error! status: ${list.ok ? people.status : list.status}`);
        const body = (await list.json()) as { timeZone: string; timeOff: TimeOff[] };
        setTimeOff(body.timeOff);
        setTimeZone(body.timeZone);
        setStaff((await people.json()) as Staff[]);
        setLoad('loaded');
      })
      .catch((err) => current && setLoadError(err instanceof Error ? err.message : 'Failed to call the API'));
    return () => {
      current = false;
    };
  }, [user, reload]);

  if (!signInEnabled) {
    return (
      <section className="card">
        <p className="hint">Sign-in isn't set up yet, so there's no business to block time for.</p>
      </section>
    );
  }
  if (!user) return <p className="status" role="status">Taking you to sign-in...</p>;
  if (loadError) return <ErrorMessage message={loadError} />;
  if (load === 'loading') return <p className="status" role="status">Loading...</p>;
  if (load === 'no-business' || load === 'not-owner') {
    return (
      <section className="card">
        <p className="hint">
          {load === 'no-business' ? "You haven't started a business yet." : 'Only owners can block time off.'}{' '}
          <a href="/start">Start your business</a>
        </p>
      </section>
    );
  }

  const save = async (event: FormEvent) => {
    event.preventDefault();
    setSaving(true);
    setErrors({});
    setFormError(undefined);
    setStatus('');
    try {
      const response = await apiFetch('/api/businesses/mine/time-off', {
        method: 'POST',
        body: JSON.stringify({
          staffMemberId: draft.staffMemberId || null,
          from: draft.allDay ? `${draft.fromDay}T00:00` : draft.from,
          to: draft.allDay ? `${nextDay(draft.toDay)}T00:00` : draft.to,
          note: draft.note || null,
        }),
      });
      if (response.ok) {
        const added = (await response.json()) as TimeOff;
        setTimeOff((list) => [...list, added].sort((a, b) => a.from.localeCompare(b.from)));
        setDraft(emptyDraft());
        setStatus(
          added.bookings.length === 0
            ? `${describe(added)} is blocked.`
            : `${describe(added)} is blocked. It has ${added.bookings.length} booking(s) in it; they're listed above.`,
        );
      } else {
        const problem = await readProblem(response);
        if (problem.errors) setErrors(problem.errors);
        else setFormError(problem.title ?? `HTTP error! status: ${response.status}`);
      }
    } catch (err) {
      setFormError(err instanceof Error ? err.message : 'Failed to call the API');
    }
    setSaving(false);
  };

  const remove = async (block: TimeOff) => {
    setBusy(block.id);
    setFormError(undefined);
    setStatus('');
    try {
      const response = await apiFetch(`/api/businesses/mine/time-off/${block.id}`, { method: 'DELETE' });
      if (!response.ok) throw new Error((await readProblem(response)).title ?? `HTTP error! status: ${response.status}`);
      setTimeOff((list) => list.filter((item) => item.id !== block.id));
      setStatus(`${describe(block)} can be booked again.`);
    } catch (err) {
      setFormError(err instanceof Error ? err.message : 'Failed to call the API');
    }
    setBusy(undefined);
  };

  // Cancelling sends the client the business's cancellation email (MVP-14).
  const cancel = async (booking: TimeOffBooking) => {
    if (!confirmCancel(booking)) return;
    setBusy(booking.id);
    setFormError(undefined);
    try {
      await cancelBooking(booking);
      setStatus(`${booking.clientName}'s booking at ${booking.start} is cancelled; they get an email.`);
      setReload((count) => count + 1);
    } catch (err) {
      setFormError(err instanceof Error ? err.message : 'Failed to call the API');
    }
    setBusy(undefined);
  };

  const field = (name: string) => ({
    'aria-invalid': Boolean(errors[name]),
    'aria-describedby': errors[name] ? `time-off-${name}-error` : undefined,
  });
  const fieldError = (name: string) =>
    errors[name] && (
      <p id={`time-off-${name}-error`} className="field-error">{errors[name][0]}</p>
    );

  return (
    <>
      {moving && (
        <MoveBooking
          what={`${moving.clientName}'s ${moving.serviceName}, ${dayLabel(moving.day)} ${moving.start}`}
          basePath={`/api/businesses/mine/bookings/${moving.id}`}
          staff={staff}
          onClose={() => setMoving(undefined)}
          onMoved={(moved) => {
            setMoving(undefined);
            setStatus(`${moving.clientName}'s booking is moved to ${dayLabel(moved.date)}, ${moved.start}; they get an email.`);
            setReload((count) => count + 1);
          }}
        />
      )}
      <section className="card" aria-labelledby="time-off-heading">
        <h2 id="time-off-heading" className="section-title">Time off</h2>
        {timeOff.length === 0 ? (
          <p className="hint">Nothing blocked. Add holidays and breaks below, and nobody can book them.</p>
        ) : (
          <ul className="service-list">
            {timeOff.map((block) => (
              <li key={block.id} className="service-item time-off-item">
                <div className="service-summary">
                  <span className="service-name">{describe(block)}</span>
                  <span className="hint">
                    {block.staffName ?? 'Whole business'}
                    {block.note && ` · ${block.note}`}
                  </span>
                  {block.bookings.length > 0 && (
                    <div className="time-off-bookings">
                      <p className="field-error">Still booked in this time: move or cancel these.</p>
                      <ul className="calendar-bookings">
                        {block.bookings.map((booking) => (
                          <li key={booking.id} className="calendar-booking">
                            <span className="calendar-time">
                              {dayLabel(booking.day)}, {booking.start}–{booking.end}
                            </span>
                            <span className="calendar-client">{booking.clientName}</span>
                            <span className="hint">{booking.serviceName} · {booking.staffName}</span>
                            <button
                              type="button"
                              className="button button-secondary calendar-cancel"
                              onClick={() => setMoving(booking)}
                            >
                              Move<span className="visually-hidden"> {booking.clientName}, {booking.start}</span>
                            </button>
                            <button
                              type="button"
                              className="button button-secondary calendar-cancel"
                              disabled={busy === booking.id}
                              onClick={() => void cancel(booking)}
                            >
                              {busy === booking.id ? 'Cancelling...' : 'Cancel booking'}
                              <span className="visually-hidden"> {booking.clientName}, {booking.start}</span>
                            </button>
                          </li>
                        ))}
                      </ul>
                    </div>
                  )}
                </div>
                <div className="service-actions">
                  <button
                    type="button"
                    className="button button-secondary"
                    disabled={busy === block.id}
                    onClick={() => void remove(block)}
                  >
                    Remove<span className="visually-hidden"> {describe(block)}</span>
                  </button>
                </div>
              </li>
            ))}
          </ul>
        )}
        <p className="field-hint" role="status">{status}</p>
      </section>

      <section className="card" aria-labelledby="time-off-form-heading">
        <h2 id="time-off-form-heading" className="section-title">Block time off</h2>
        <form className="form" onSubmit={(event) => void save(event)} noValidate>
          <div className="field">
            <label htmlFor="time-off-staff">Who</label>
            <select
              id="time-off-staff"
              className="input input-narrow"
              value={draft.staffMemberId}
              {...field('staffMemberId')}
              onChange={(event) => setDraft({ ...draft, staffMemberId: event.target.value })}
            >
              <option value="">Whole business</option>
              {staff.map((member) => (
                <option key={member.id} value={member.id}>{member.name}</option>
              ))}
            </select>
            {fieldError('staffMemberId')}
          </div>

          <label className="choice">
            <input
              type="checkbox"
              checked={draft.allDay}
              onChange={(event) => setDraft({ ...draft, allDay: event.target.checked })}
            />
            Whole days
          </label>

          {draft.allDay ? (
            <div className="price-input">
              <div className="field">
                <label htmlFor="time-off-from-day">First day</label>
                <input
                  id="time-off-from-day"
                  className="input input-narrow"
                  type="date"
                  value={draft.fromDay}
                  {...field('from')}
                  onChange={(event) => setDraft({ ...draft, fromDay: event.target.value })}
                />
              </div>
              <div className="field">
                <label htmlFor="time-off-to-day">Last day</label>
                <input
                  id="time-off-to-day"
                  className="input input-narrow"
                  type="date"
                  value={draft.toDay}
                  min={draft.fromDay}
                  {...field('to')}
                  onChange={(event) => setDraft({ ...draft, toDay: event.target.value })}
                />
              </div>
            </div>
          ) : (
            <div className="price-input">
              <div className="field">
                <label htmlFor="time-off-from">From</label>
                <input
                  id="time-off-from"
                  className="input input-narrow"
                  type="datetime-local"
                  step={300}
                  value={draft.from}
                  {...field('from')}
                  onChange={(event) => setDraft({ ...draft, from: event.target.value })}
                />
              </div>
              <div className="field">
                <label htmlFor="time-off-to">Until</label>
                <input
                  id="time-off-to"
                  className="input input-narrow"
                  type="datetime-local"
                  step={300}
                  value={draft.to}
                  min={draft.from}
                  {...field('to')}
                  onChange={(event) => setDraft({ ...draft, to: event.target.value })}
                />
              </div>
            </div>
          )}
          {fieldError('from')}
          {fieldError('to')}
          <p className="field-hint">In the business's time zone, {timeZone.replace(/_/g, ' ')}.</p>

          <div className="field">
            <label htmlFor="time-off-note">Note, only for you</label>
            <input
              id="time-off-note"
              className="input"
              maxLength={200}
              placeholder="e.g. Holiday"
              value={draft.note}
              {...field('note')}
              onChange={(event) => setDraft({ ...draft, note: event.target.value })}
            />
            {fieldError('note')}
          </div>

          {formError && <ErrorMessage message={formError} />}

          <div className="form-actions">
            <button className="button" type="submit" disabled={saving}>
              {saving ? 'Saving...' : 'Block this time'}
            </button>
          </div>
        </form>
      </section>
    </>
  );
}
