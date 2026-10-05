import { useEffect, useState, type FormEvent } from 'react';
import { apiFetch, readProblem } from './api.ts';
import { signInUrl, useSession } from './session.ts';
import { ErrorMessage } from './ui.tsx';

const days = [
  { key: 'monday', label: 'Monday' },
  { key: 'tuesday', label: 'Tuesday' },
  { key: 'wednesday', label: 'Wednesday' },
  { key: 'thursday', label: 'Thursday' },
  { key: 'friday', label: 'Friday' },
  { key: 'saturday', label: 'Saturday' },
  { key: 'sunday', label: 'Sunday' },
] as const;

type Day = (typeof days)[number]['key'];

// Times as HH:mm, in the business's time zone: what <input type="time"> and the API both use.
type Period = { opens: string; closes: string };

type Week = Record<Day, Period[]>;

type OpeningHoursBody = {
  timeZone: string;
  periods: { day: Day; opens: string; closes: string }[];
};

type Load = 'loading' | 'loaded' | 'no-business' | 'not-owner';

const emptyWeek = (): Week => ({
  monday: [], tuesday: [], wednesday: [], thursday: [], friday: [], saturday: [], sunday: [],
});

function toWeek(body: OpeningHoursBody): Week {
  const week = emptyWeek();
  for (const { day, opens, closes } of body.periods) week[day].push({ opens, closes });
  return week;
}

const toMinutes = (time: string) => Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5));
const toTime = (minutes: number) =>
  `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`;

// A new period starts an hour after the day's last one ends (a lunch break), or at 9:00.
function nextPeriod(periods: Period[]): Period {
  const last = periods[periods.length - 1];
  if (!last) return { opens: '09:00', closes: '17:00' };
  const opens = Math.min(toMinutes(last.closes) + 60, 23 * 60);
  return { opens: toTime(opens), closes: toTime(Math.min(opens + 4 * 60, 23 * 60 + 55)) };
}

// User story MVP-9: the owner sets the weekly opening hours, several periods a day if needed.
export function OpeningHoursPage() {
  const { signInEnabled, user } = useSession();
  const [load, setLoad] = useState<Load>('loading');
  const [loadError, setLoadError] = useState<string>();
  const [week, setWeek] = useState<Week>(emptyWeek);
  const [timeZone, setTimeZone] = useState('');
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [formError, setFormError] = useState<string>();
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    if (signInEnabled && !user) window.location.replace(signInUrl('/hours'));
  }, [signInEnabled, user]);

  useEffect(() => {
    if (!user) return;
    let current = true;
    apiFetch('/api/businesses/mine/opening-hours')
      .then(async (response) => {
        if (!current) return;
        if (response.status === 404) return setLoad('no-business');
        if (response.status === 403) return setLoad('not-owner');
        if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);
        const body = (await response.json()) as OpeningHoursBody;
        setWeek(toWeek(body));
        setTimeZone(body.timeZone);
        setLoad('loaded');
      })
      .catch((err) => current && setLoadError(err instanceof Error ? err.message : 'Failed to call the API'));
    return () => {
      current = false;
    };
  }, [user]);

  if (!signInEnabled) {
    return (
      <section className="card">
        <p className="hint">Sign-in isn't set up yet, so there are no businesses to set hours for.</p>
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
          {load === 'no-business'
            ? "You haven't started a business yet."
            : 'Only owners can set opening hours.'}{' '}
          <a href="/start">Start your business</a>
        </p>
      </section>
    );
  }

  const change = (day: Day, periods: Period[]) => {
    setWeek({ ...week, [day]: periods });
    setSaved(false);
  };

  // The browser's list, plus the stored zone in case the browser doesn't list it (e.g. UTC).
  const timeZones = Intl.supportedValuesOf('timeZone');
  if (timeZone && !timeZones.includes(timeZone)) timeZones.unshift(timeZone);

  const save = async (event: FormEvent) => {
    event.preventDefault();
    setSaving(true);
    setErrors({});
    setFormError(undefined);
    try {
      const body: OpeningHoursBody = {
        timeZone,
        periods: days.flatMap(({ key }) => week[key].map((period) => ({ day: key, ...period }))),
      };
      const response = await apiFetch('/api/businesses/mine/opening-hours', {
        method: 'PUT',
        body: JSON.stringify(body),
      });
      if (response.ok) {
        setWeek(toWeek((await response.json()) as OpeningHoursBody));
        setSaved(true);
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

  return (
    <section className="card" aria-labelledby="hours-heading">
      <h2 id="hours-heading" className="section-title">Opening hours</h2>
      <p className="hint">
        Clients can only book when you're open. Changing the hours doesn't cancel bookings you already have.
      </p>
      <form className="form" onSubmit={(event) => void save(event)} noValidate>
        <div className="field">
          <label htmlFor="time-zone">Time zone</label>
          <select
            id="time-zone"
            className="input"
            value={timeZone}
            onChange={(event) => {
              setTimeZone(event.target.value);
              setSaved(false);
            }}
            aria-invalid={Boolean(errors.timeZone)}
          >
            {timeZones.map((zone) => (
              <option key={zone} value={zone}>{zone.replace(/_/g, ' ')}</option>
            ))}
          </select>
          {errors.timeZone && <p className="field-error">{errors.timeZone[0]}</p>}
        </div>

        {days.map(({ key, label }) => (
          <fieldset key={key} className="day" aria-describedby={errors[key] ? `${key}-errors` : undefined}>
            <legend className="day-name">{label}</legend>
            <div className="day-periods">
              {week[key].length === 0 && <span className="hint">Closed</span>}
              {week[key].map((period, index) => (
                <div key={index} className="period">
                  <input
                    type="time"
                    step={300}
                    className="input"
                    value={period.opens}
                    aria-label={`${label}, period ${index + 1}, opens`}
                    onChange={(event) =>
                      change(key, week[key].map((p, i) => (i === index ? { ...p, opens: event.target.value } : p)))
                    }
                  />
                  <span aria-hidden="true">–</span>
                  <input
                    type="time"
                    step={300}
                    className="input"
                    value={period.closes}
                    aria-label={`${label}, period ${index + 1}, closes`}
                    onChange={(event) =>
                      change(key, week[key].map((p, i) => (i === index ? { ...p, closes: event.target.value } : p)))
                    }
                  />
                  <button
                    type="button"
                    className="button button-secondary"
                    aria-label={`Remove ${label}, period ${index + 1}`}
                    onClick={() => change(key, week[key].filter((_, i) => i !== index))}
                  >
                    Remove
                  </button>
                </div>
              ))}
              <div>
                <button
                  type="button"
                  className="button button-secondary"
                  onClick={() => change(key, [...week[key], nextPeriod(week[key])])}
                >
                  {week[key].length === 0 ? 'Open this day' : 'Add hours'}
                </button>
              </div>
              {errors[key] && (
                <div id={`${key}-errors`}>
                  {errors[key].map((message) => (
                    <p key={message} className="field-error">{message}</p>
                  ))}
                </div>
              )}
            </div>
          </fieldset>
        ))}

        {errors.periods && <ErrorMessage message={errors.periods[0]} />}
        {formError && <ErrorMessage message={formError} />}

        <div className="form-actions">
          <button className="button" type="submit" disabled={saving}>
            {saving ? 'Saving...' : 'Save opening hours'}
          </button>
          <span className="field-hint" role="status">{saved ? 'Saved.' : ''}</span>
          <a href="/">Back to your business</a>
        </div>
      </form>
    </section>
  );
}
