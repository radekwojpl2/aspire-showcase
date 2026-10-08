import { useEffect, useState } from 'react';
import { apiFetch, readProblem } from './api.ts';
import { ErrorMessage } from './ui.tsx';

type Slot = { start: string; startsAt: string };
type Day = { date: string; slots: Slot[] };

const dayLabel = (day: string) =>
  new Date(`${day}T00:00:00Z`).toLocaleDateString(undefined, {
    weekday: 'short',
    day: 'numeric',
    month: 'short',
    timeZone: 'UTC',
  });

// How many days with free times show at first, and with each "More days".
const daysShown = 5;

// The free times the API lists at `url`, in the business's time zone, to choose one from: for
// moving a booking (V1-4) or the owner booking someone who phoned (V1-5). Whoever renders it
// clears the choice when what it asks for changes.
export function SlotPicker({
  url,
  startsAt,
  onChoose,
}: {
  url: string;
  startsAt?: string;
  onChoose: (startsAt: string) => void;
}) {
  const [days, setDays] = useState<Day[]>();
  const [shown, setShown] = useState(daysShown);
  const [timeZone, setTimeZone] = useState('');
  const [error, setError] = useState<string>();
  const [loading, setLoading] = useState(false);

  // The previous times stay on screen, disabled, until the new ones come: clearing them would
  // collapse the list and make the page jump on every change of service or staff.
  useEffect(() => {
    let current = true;
    setLoading(true);
    apiFetch(url)
      .then(async (response) => {
        if (!response.ok) throw new Error((await readProblem(response)).title ?? `HTTP error! status: ${response.status}`);
        const body = (await response.json()) as { timeZone: string; days: Day[] };
        if (!current) return;
        setError(undefined);
        setShown(daysShown);
        setTimeZone(body.timeZone);
        setDays(body.days);
      })
      .catch((err) => current && setError(err instanceof Error ? err.message : 'Failed to call the API'))
      .finally(() => current && setLoading(false));
    return () => {
      current = false;
    };
  }, [url]);

  if (error) return <ErrorMessage message={error} />;
  if (!days) return <p className="status" role="status">Loading...</p>;
  if (days.length === 0) return <p className="hint">No free times in the next 4 weeks.</p>;

  return (
    <>
      <p className="hint">Times in {timeZone.replace(/_/g, ' ')}</p>
      <div className="slot-days" aria-busy={loading}>
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
                  disabled={loading}
                  onClick={() => onChoose(slot.startsAt)}
                >
                  {slot.start}
                </button>
              ))}
            </div>
          </div>
        ))}
      </div>
      {shown < days.length && (
        <div className="form-actions">
          <button type="button" className="button button-secondary" onClick={() => setShown(shown + daysShown)}>
            More days
          </button>
        </div>
      )}
    </>
  );
}
