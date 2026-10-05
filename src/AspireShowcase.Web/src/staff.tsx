import { useEffect, useState, type FormEvent } from 'react';
import { apiFetch, readProblem } from './api.ts';
import { signInUrl, useSession } from './session.ts';
import { ErrorMessage } from './ui.tsx';
import { WeekEditor } from './week-editor.tsx';
import { describe, emptyWeek, toPeriods, toWeek, type DayPeriod, type Week } from './weekly-hours.ts';

type StaffMember = {
  id: string;
  name: string;
  isOwner: boolean;
  doesAllServices: boolean;
  serviceIds: string[];
  // Null: works whenever the business is open.
  workingHours: DayPeriod[] | null;
};

type Service = { id: string; name: string; isHidden: boolean };

type Draft = {
  name: string;
  doesAllServices: boolean;
  serviceIds: string[];
  ownHours: boolean;
  week: Week;
};

type Load = 'loading' | 'loaded' | 'no-business' | 'not-owner';

// User story MVP-11: the owner adds staff, with the services they do and their working hours.
export function StaffPage() {
  const { signInEnabled, user } = useSession();
  const [load, setLoad] = useState<Load>('loading');
  const [loadError, setLoadError] = useState<string>();
  const [staff, setStaff] = useState<StaffMember[]>([]);
  const [services, setServices] = useState<Service[]>([]);
  // New working hours start from the opening hours, the most they can be.
  const [openingWeek, setOpeningWeek] = useState<Week>(emptyWeek);
  // The staff member being edited, or null while adding someone.
  const [editing, setEditing] = useState<string | null>(null);
  const [draft, setDraft] = useState<Draft>(() => blankDraft(emptyWeek()));
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [formError, setFormError] = useState<string>();
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (signInEnabled && !user) window.location.replace(signInUrl('/staff'));
  }, [signInEnabled, user]);

  useEffect(() => {
    if (!user) return;
    let current = true;
    const get = (path: string) => apiFetch(`/api/businesses/mine/${path}`);
    Promise.all([get('staff'), get('services'), get('opening-hours')])
      .then(async (responses) => {
        if (!current) return;
        if (responses.some((response) => response.status === 404)) return setLoad('no-business');
        if (responses.some((response) => response.status === 403)) return setLoad('not-owner');
        const failed = responses.find((response) => !response.ok);
        if (failed) throw new Error(`HTTP error! status: ${failed.status}`);
        const [staffList, serviceList, hours] = await Promise.all(responses.map((response) => response.json()));
        const opening = toWeek((hours as { periods: DayPeriod[] }).periods);
        setStaff(staffList as StaffMember[]);
        setServices(serviceList as Service[]);
        setOpeningWeek(opening);
        setDraft(blankDraft(opening));
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
        <p className="hint">Sign-in isn't set up yet, so there are no businesses to add staff to.</p>
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
          {load === 'no-business' ? "You haven't started a business yet." : 'Only owners can manage staff.'}{' '}
          <a href="/start">Start your business</a>
        </p>
      </section>
    );
  }

  const serviceName = (id: string) => services.find((service) => service.id === id)?.name ?? 'a removed service';

  const startEditing = (member: StaffMember | null) => {
    setEditing(member?.id ?? null);
    setDraft(
      member
        ? {
            name: member.name,
            doesAllServices: member.doesAllServices,
            serviceIds: member.serviceIds,
            ownHours: member.workingHours !== null,
            week: member.workingHours ? toWeek(member.workingHours) : openingWeek,
          }
        : blankDraft(openingWeek),
    );
    setErrors({});
    setFormError(undefined);
  };

  const toggleService = (id: string, checked: boolean) =>
    setDraft({
      ...draft,
      serviceIds: checked ? [...draft.serviceIds, id] : draft.serviceIds.filter((serviceId) => serviceId !== id),
    });

  const save = async (event: FormEvent) => {
    event.preventDefault();
    setSaving(true);
    setErrors({});
    setFormError(undefined);
    try {
      const response = await apiFetch(
        editing ? `/api/businesses/mine/staff/${editing}` : '/api/businesses/mine/staff',
        {
          method: editing ? 'PUT' : 'POST',
          body: JSON.stringify({
            name: draft.name,
            doesAllServices: draft.doesAllServices,
            serviceIds: draft.doesAllServices ? [] : draft.serviceIds,
            workingHours: draft.ownHours ? toPeriods(draft.week) : null,
          }),
        },
      );
      if (response.ok) {
        const saved = (await response.json()) as StaffMember;
        setStaff((list) => (editing ? list.map((member) => (member.id === saved.id ? saved : member)) : [...list, saved]));
        setEditing(null);
        setDraft(blankDraft(openingWeek));
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
    <>
      <section className="card" aria-labelledby="staff-heading">
        <h2 id="staff-heading" className="section-title">Staff</h2>
        <p className="hint">Clients can book different staff for the same time, each with their own bookings.</p>
        <ul className="service-list">
          {staff.map((member) => (
            <li key={member.id} className="service-item">
              <div className="service-summary">
                <span className="service-name">
                  {member.name}
                  {member.isOwner && <span className="badge">You</span>}
                </span>
                <span className="hint">
                  {member.doesAllServices ? 'All services' : member.serviceIds.map(serviceName).join(', ')}
                </span>
                <span className="hint">
                  {member.workingHours === null
                    ? 'Whenever the business is open'
                    : describe(member.workingHours) || 'No working hours'}
                </span>
              </div>
              <div className="service-actions">
                <button type="button" className="button button-secondary" onClick={() => startEditing(member)}>
                  Edit<span className="visually-hidden"> {member.name}</span>
                </button>
              </div>
            </li>
          ))}
        </ul>
      </section>

      <section className="card" aria-labelledby="staff-form-heading">
        <h2 id="staff-form-heading" className="section-title">
          {editing ? 'Edit staff member' : 'Add a staff member'}
        </h2>
        <form className="form" onSubmit={(event) => void save(event)} noValidate>
          <div className="field">
            <label htmlFor="staff-name">Name, as clients see it</label>
            <input
              id="staff-name"
              className="input"
              maxLength={80}
              value={draft.name}
              aria-invalid={Boolean(errors.name)}
              aria-describedby={errors.name ? 'staff-name-error' : undefined}
              onChange={(event) => setDraft({ ...draft, name: event.target.value })}
            />
            {errors.name && <p id="staff-name-error" className="field-error">{errors.name[0]}</p>}
          </div>

          <fieldset className="field choice-group">
            <legend>Services</legend>
            <label className="choice">
              <input
                type="checkbox"
                checked={draft.doesAllServices}
                onChange={(event) => setDraft({ ...draft, doesAllServices: event.target.checked })}
              />
              All services, including ones you add later
            </label>
            {!draft.doesAllServices &&
              (services.length === 0 ? (
                <p className="hint">
                  No services yet. <a href="/services">Add services</a>
                </p>
              ) : (
                services.map((service) => (
                  <label key={service.id} className="choice choice-nested">
                    <input
                      type="checkbox"
                      checked={draft.serviceIds.includes(service.id)}
                      onChange={(event) => toggleService(service.id, event.target.checked)}
                    />
                    {service.name}
                    {service.isHidden && <span className="badge badge-muted">Hidden</span>}
                  </label>
                ))
              ))}
            {errors.serviceIds && <p className="field-error">{errors.serviceIds[0]}</p>}
          </fieldset>

          <fieldset className="field choice-group">
            <legend>Working hours</legend>
            <label className="choice">
              <input
                type="checkbox"
                checked={!draft.ownHours}
                onChange={(event) =>
                  setDraft({ ...draft, ownHours: !event.target.checked, week: draft.ownHours ? draft.week : openingWeek })
                }
              />
              Whenever the business is open
            </label>
            {draft.ownHours && (
              <>
                <p className="hint">Within the opening hours.</p>
                <WeekEditor
                  week={draft.week}
                  onChange={(week) => setDraft({ ...draft, week })}
                  errors={errors}
                  idPrefix="staff"
                  emptyDay="Not working"
                  addFirst="Work this day"
                  noun={{ start: 'starts', end: 'ends' }}
                />
              </>
            )}
          </fieldset>

          {errors.periods && <ErrorMessage message={errors.periods[0]} />}
          {formError && <ErrorMessage message={formError} />}

          <div className="form-actions">
            <button className="button" type="submit" disabled={saving}>
              {saving ? 'Saving...' : editing ? 'Save changes' : 'Add staff member'}
            </button>
            {editing && (
              <button type="button" className="button button-secondary" onClick={() => startEditing(null)}>
                Cancel
              </button>
            )}
            <a href="/">Back to your business</a>
          </div>
        </form>
      </section>
    </>
  );
}

function blankDraft(openingWeek: Week): Draft {
  return { name: '', doesAllServices: true, serviceIds: [], ownHours: false, week: openingWeek };
}
