import { useEffect, useState, type FormEvent } from 'react';
import { apiFetch, readProblem } from './api.ts';
import { signInUrl, useSession } from './session.ts';
import { ErrorMessage } from './ui.tsx';

type Service = {
  id: string;
  name: string;
  durationMinutes: number;
  price: number;
  currency: string;
  isHidden: boolean;
};

// What the form edits; strings, as the inputs hold them.
type Draft = { name: string; durationMinutes: string; price: string; currency: string };

type Load = 'loading' | 'loaded' | 'no-business' | 'not-owner';

const formatDuration = (minutes: number) => {
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return [hours && `${hours} h`, rest && `${rest} min`].filter(Boolean).join(' ');
};

const formatPrice = (service: Service) =>
  new Intl.NumberFormat(undefined, { style: 'currency', currency: service.currency }).format(service.price);

// The currency the business already uses most, so a new service doesn't have to pick it again.
function usualCurrency(services: Service[]): string {
  const counts = new Map<string, number>();
  for (const { currency } of services) counts.set(currency, (counts.get(currency) ?? 0) + 1);
  return [...counts].sort((a, b) => b[1] - a[1])[0]?.[0] ?? 'EUR';
}

const emptyDraft = (services: Service[]): Draft => ({
  name: '',
  durationMinutes: '30',
  price: '',
  currency: usualCurrency(services),
});

// User story MVP-10: the owner adds services with a duration and a price, and hides them
// instead of deleting them.
export function ServicesPage() {
  const { signInEnabled, user } = useSession();
  const [load, setLoad] = useState<Load>('loading');
  const [loadError, setLoadError] = useState<string>();
  const [services, setServices] = useState<Service[]>([]);
  // The service being edited, or null while adding a new one.
  const [editing, setEditing] = useState<string | null>(null);
  const [draft, setDraft] = useState<Draft>(() => emptyDraft([]));
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [formError, setFormError] = useState<string>();
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (signInEnabled && !user) window.location.replace(signInUrl('/services'));
  }, [signInEnabled, user]);

  useEffect(() => {
    if (!user) return;
    let current = true;
    apiFetch('/api/businesses/mine/services')
      .then(async (response) => {
        if (!current) return;
        if (response.status === 404) return setLoad('no-business');
        if (response.status === 403) return setLoad('not-owner');
        if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);
        const list = (await response.json()) as Service[];
        setServices(list);
        setDraft(emptyDraft(list));
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
        <p className="hint">Sign-in isn't set up yet, so there are no businesses to add services to.</p>
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
          {load === 'no-business' ? "You haven't started a business yet." : 'Only owners can manage services.'}{' '}
          <a href="/start">Start your business</a>
        </p>
      </section>
    );
  }

  const replace = (saved: Service) =>
    setServices((list) =>
      [...list.filter((service) => service.id !== saved.id), saved].sort((a, b) => a.name.localeCompare(b.name)),
    );

  const startEditing = (service: Service | null) => {
    setEditing(service?.id ?? null);
    setDraft(
      service
        ? {
            name: service.name,
            durationMinutes: String(service.durationMinutes),
            price: String(service.price),
            currency: service.currency,
          }
        : emptyDraft(services),
    );
    setErrors({});
    setFormError(undefined);
  };

  const save = async (event: FormEvent) => {
    event.preventDefault();
    setSaving(true);
    setErrors({});
    setFormError(undefined);
    try {
      const response = await apiFetch(
        editing ? `/api/businesses/mine/services/${editing}` : '/api/businesses/mine/services',
        {
          method: editing ? 'PUT' : 'POST',
          body: JSON.stringify({
            name: draft.name,
            durationMinutes: draft.durationMinutes ? Number(draft.durationMinutes) : null,
            price: draft.price ? Number(draft.price) : null,
            currency: draft.currency,
          }),
        },
      );
      if (response.ok) {
        const saved = (await response.json()) as Service;
        replace(saved);
        setEditing(null);
        setDraft(emptyDraft([...services, saved]));
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

  const setVisibility = async (service: Service) => {
    setFormError(undefined);
    try {
      const action = service.isHidden ? 'show' : 'hide';
      const response = await apiFetch(`/api/businesses/mine/services/${service.id}/${action}`, { method: 'POST' });
      if (!response.ok) throw new Error((await readProblem(response)).title ?? `HTTP error! status: ${response.status}`);
      replace((await response.json()) as Service);
    } catch (err) {
      setFormError(err instanceof Error ? err.message : 'Failed to call the API');
    }
  };

  const field = (name: keyof Draft) => ({
    value: draft[name],
    'aria-invalid': Boolean(errors[name]),
    'aria-describedby': errors[name] ? `service-${name}-error` : undefined,
  });
  const fieldError = (name: string) =>
    errors[name] && (
      <p id={`service-${name}-error`} className="field-error">{errors[name][0]}</p>
    );

  return (
    <>
      <section className="card" aria-labelledby="services-heading">
        <h2 id="services-heading" className="section-title">Services</h2>
        {services.length === 0 ? (
          <p className="hint">No services yet. Add what clients can book, below.</p>
        ) : (
          <ul className="service-list">
            {services.map((service) => (
              <li key={service.id} className={`service-item ${service.isHidden ? 'hidden-service' : ''}`}>
                <div className="service-summary">
                  <span className="service-name">
                    {service.name}
                    {service.isHidden && <span className="badge badge-muted">Hidden</span>}
                  </span>
                  <span className="hint">
                    {formatDuration(service.durationMinutes)} · {formatPrice(service)}
                  </span>
                </div>
                <div className="service-actions">
                  <button type="button" className="button button-secondary" onClick={() => startEditing(service)}>
                    Edit<span className="visually-hidden"> {service.name}</span>
                  </button>
                  <button type="button" className="button button-secondary" onClick={() => void setVisibility(service)}>
                    {service.isHidden ? 'Show' : 'Hide'}
                    <span className="visually-hidden"> {service.name}</span>
                  </button>
                </div>
              </li>
            ))}
          </ul>
        )}
        <p className="hint">Hidden services aren't offered to clients, and you can show them again any time.</p>
      </section>

      <section className="card" aria-labelledby="service-form-heading">
        <h2 id="service-form-heading" className="section-title">
          {editing ? 'Edit service' : 'Add a service'}
        </h2>
        <form className="form" onSubmit={(event) => void save(event)} noValidate>
          <div className="field">
            <label htmlFor="service-name">Name</label>
            <input
              id="service-name"
              className="input"
              maxLength={80}
              placeholder="e.g. Haircut"
              {...field('name')}
              onChange={(event) => setDraft({ ...draft, name: event.target.value })}
            />
            {fieldError('name')}
          </div>

          <div className="field">
            <label htmlFor="service-duration">Duration, in minutes</label>
            <input
              id="service-duration"
              className="input input-narrow"
              type="number"
              min={5}
              max={480}
              step={5}
              {...field('durationMinutes')}
              onChange={(event) => setDraft({ ...draft, durationMinutes: event.target.value })}
            />
            {fieldError('durationMinutes')}
          </div>

          <div className="field">
            <label htmlFor="service-price">Price shown to clients</label>
            <div className="price-input">
              <input
                id="service-price"
                className="input input-narrow"
                type="number"
                min={0}
                step={0.01}
                inputMode="decimal"
                {...field('price')}
                onChange={(event) => setDraft({ ...draft, price: event.target.value })}
              />
              <select
                className="input input-narrow"
                aria-label="Currency"
                {...field('currency')}
                onChange={(event) => setDraft({ ...draft, currency: event.target.value })}
              >
                {Intl.supportedValuesOf('currency').map((code) => (
                  <option key={code} value={code}>{code}</option>
                ))}
              </select>
            </div>
            {fieldError('price')}
            {fieldError('currency')}
            <p className="field-hint">Clients pay you in person; nothing is paid online.</p>
          </div>

          {formError && <ErrorMessage message={formError} />}

          <div className="form-actions">
            <button className="button" type="submit" disabled={saving}>
              {saving ? 'Saving...' : editing ? 'Save changes' : 'Add service'}
            </button>
            {editing && (
              <button type="button" className="button button-secondary" onClick={() => startEditing(null)}>
                Cancel
              </button>
            )}
          </div>
        </form>
      </section>
    </>
  );
}
