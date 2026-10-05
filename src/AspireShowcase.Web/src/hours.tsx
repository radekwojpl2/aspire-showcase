import { useEffect, useState, type FormEvent } from 'react';
import { apiFetch, readProblem } from './api.ts';
import { signInUrl, useSession } from './session.ts';
import { ErrorMessage } from './ui.tsx';
import { WeekEditor } from './week-editor.tsx';
import { emptyWeek, toPeriods, toWeek, type DayPeriod, type Week } from './weekly-hours.ts';

type OpeningHoursBody = {
  timeZone: string;
  periods: DayPeriod[];
};

type Load = 'loading' | 'loaded' | 'no-business' | 'not-owner';

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
        setWeek(toWeek(body.periods));
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

  const change = (changed: Week) => {
    setWeek(changed);
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
        periods: toPeriods(week),
      };
      const response = await apiFetch('/api/businesses/mine/opening-hours', {
        method: 'PUT',
        body: JSON.stringify(body),
      });
      if (response.ok) {
        setWeek(toWeek(((await response.json()) as OpeningHoursBody).periods));
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

        <WeekEditor
          week={week}
          onChange={change}
          errors={errors}
          idPrefix="opening"
          emptyDay="Closed"
          addFirst="Open this day"
        />

        {/* Narrowing the hours past someone's own working hours is refused, naming who. */}
        {errors.staff?.map((message) => <ErrorMessage key={message} message={message} />)}
        {errors.periods && <ErrorMessage message={errors.periods[0]} />}
        {formError && <ErrorMessage message={formError} />}

        <div className="form-actions">
          <button className="button" type="submit" disabled={saving}>
            {saving ? 'Saving...' : 'Save opening hours'}
          </button>
          <span className="field-hint" role="status">{saved ? 'Saved.' : ''}</span>
        </div>
      </form>
    </section>
  );
}
