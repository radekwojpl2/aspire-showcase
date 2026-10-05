import { useEffect, useState, type FormEvent } from 'react';
import { apiFetch, readProblem } from './api.ts';
import { signInUrl, useSession } from './session.ts';
import { ErrorMessage } from './ui.tsx';

type Business = {
  id: string;
  name: string;
  slug: string;
  timeZone: string;
  createdAt: string;
};

type Availability =
  | { state: 'idle' | 'checking' | 'available' }
  | { state: 'unavailable'; problem: string };

const maxSlugLength = 40;

const bookingLink = (slug: string) => `${window.location.origin}/book/${slug}`;

// "Anna's Hair & Nails" -> "annas-hair-nails": what the API accepts in a booking link.
function slugify(name: string): string {
  return name
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/['’]/g, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, maxSlugLength)
    .replace(/-+$/, '');
}

// The signed-in user's business: undefined while loading, null when they have none.
function useMyBusiness(enabled: boolean) {
  const [business, setBusiness] = useState<Business | null>();
  const [error, setError] = useState<string>();

  useEffect(() => {
    if (!enabled) return;
    let current = true;
    apiFetch('/api/businesses/mine')
      .then(async (response) => {
        if (response.status === 404) return null;
        if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);
        return (await response.json()) as Business;
      })
      .then((result) => current && setBusiness(result))
      .catch((err) => current && setError(err instanceof Error ? err.message : 'Failed to call the API'));
    return () => {
      current = false;
    };
  }, [enabled]);

  return { business, error };
}

function BusinessCard({ business }: { business: Business }) {
  return (
    <section className="card" aria-labelledby="business-heading">
      <h2 id="business-heading" className="section-title">{business.name}</h2>
      <p className="hint">Share this link, and clients can book with you:</p>
      <p className="booking-link">{bookingLink(business.slug)}</p>
      <div className="form-actions">
        <a className="button" href="/bookings">
          Bookings
        </a>
        <a className="button button-secondary" href="/hours">
          Opening hours
        </a>
        <a className="button button-secondary" href="/services">
          Services
        </a>
        <a className="button button-secondary" href="/staff">
          Staff
        </a>
      </div>
    </section>
  );
}

function StartCard() {
  return (
    <section className="card" aria-labelledby="start-heading">
      <h2 id="start-heading" className="section-title">Take bookings online</h2>
      <p className="hint">
        Set up your business, share its booking link, and let clients book a time with you.
      </p>
      <div>
        <a className="button" href="/start">
          Start your business
        </a>
      </div>
    </section>
  );
}

// The home page: the owner's business, or the way to start one.
export function Home() {
  const { signInEnabled, user } = useSession();
  const { business, error } = useMyBusiness(user !== null);

  if (!signInEnabled) {
    return (
      <section className="card">
        <p className="hint">Sign-in isn't set up yet, so businesses can't be started. See the README.</p>
      </section>
    );
  }
  if (error) return <ErrorMessage message={error} />;
  if (user && business === undefined) return <p className="status" role="status">Loading...</p>;
  return business ? <BusinessCard business={business} /> : <StartCard />;
}

// User story MVP-8: sign up (or in), then create the business with a name and a booking link.
export function StartBusiness() {
  const { signInEnabled, user } = useSession();
  const { business, error: loadError } = useMyBusiness(user !== null);

  const [name, setName] = useState('');
  const [slug, setSlug] = useState('');
  // Until the link is edited by hand, it follows the name.
  const [slugEdited, setSlugEdited] = useState(false);
  const [availability, setAvailability] = useState<Availability>({ state: 'idle' });
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [formError, setFormError] = useState<string>();
  const [submitting, setSubmitting] = useState(false);

  // Starting a business needs an account: send signed-out visitors to Logto's sign-up form.
  useEffect(() => {
    if (signInEnabled && !user) window.location.replace(signInUrl('/start', true));
  }, [signInEnabled, user]);

  // Asks whether the link is free once typing pauses, so a taken one shows before submitting.
  useEffect(() => {
    if (!slug) {
      setAvailability({ state: 'idle' });
      return;
    }
    setAvailability({ state: 'checking' });
    const controller = new AbortController();
    const timer = setTimeout(() => {
      apiFetch(`/api/businesses/slug-availability?${new URLSearchParams({ slug })}`, { signal: controller.signal })
        .then((response) => (response.ok ? response.json() : Promise.reject(new Error())))
        .then((result: { available: boolean; problem: string | null }) =>
          setAvailability(
            result.available ? { state: 'available' } : { state: 'unavailable', problem: result.problem ?? '' },
          ),
        )
        // Not knowing isn't an error: submitting checks again.
        .catch(() => !controller.signal.aborted && setAvailability({ state: 'idle' }));
    }, 300);
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [slug]);

  if (!signInEnabled) {
    return (
      <section className="card">
        <p className="hint">Sign-in isn't set up yet, so businesses can't be started.</p>
      </section>
    );
  }
  if (!user) return <p className="status" role="status">Taking you to sign-up...</p>;
  if (loadError) return <ErrorMessage message={loadError} />;
  if (business === undefined) return <p className="status" role="status">Loading...</p>;
  if (business) {
    return (
      <>
        <BusinessCard business={business} />
        <p className="hint">
          You already have a business. <a href="/">Back to the start page</a>
        </p>
      </>
    );
  }

  const changeName = (value: string) => {
    setName(value);
    if (!slugEdited) setSlug(slugify(value));
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setSubmitting(true);
    setErrors({});
    setFormError(undefined);
    try {
      const response = await apiFetch('/api/businesses', {
        method: 'POST',
        // Opening hours are in the business's time zone; the browser's is the likely one, and
        // the owner can change it with the hours.
        // The owner becomes the first staff member, under the name they signed in with.
        body: JSON.stringify({
          name,
          slug,
          timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
          ownerName: user?.name,
        }),
      });
      if (response.ok) {
        // Sign in again, so the new owner role is in the access token. Logto still has the
        // user signed in, so this is a few redirects, not another password.
        window.location.assign(signInUrl('/'));
        return;
      }
      const problem = await readProblem(response);
      if (problem.errors) setErrors(problem.errors);
      else setFormError(problem.title ?? `HTTP error! status: ${response.status}`);
    } catch (err) {
      setFormError(err instanceof Error ? err.message : 'Failed to call the API');
    }
    setSubmitting(false);
  };

  const slugMessage =
    errors.slug?.[0] ??
    (availability.state === 'unavailable'
      ? availability.problem
      : availability.state === 'available'
        ? 'This link is free.'
        : undefined);
  const slugInvalid = Boolean(errors.slug) || availability.state === 'unavailable';

  return (
    <section className="card" aria-labelledby="start-heading">
      <h2 id="start-heading" className="section-title">Start your business</h2>
      <form className="form" onSubmit={(event) => void submit(event)} noValidate>
        <div className="field">
          <label htmlFor="business-name">Business name</label>
          <input
            id="business-name"
            className="input"
            value={name}
            onChange={(event) => changeName(event.target.value)}
            maxLength={100}
            autoComplete="organization"
            aria-invalid={Boolean(errors.name)}
            aria-describedby={errors.name ? 'business-name-error' : undefined}
            required
          />
          {errors.name && (
            <p id="business-name-error" className="field-error">{errors.name[0]}</p>
          )}
        </div>

        <div className="field">
          <label htmlFor="business-slug">Booking link</label>
          <div className="slug-input">
            <span className="slug-prefix" aria-hidden="true">{bookingLink('')}</span>
            <input
              id="business-slug"
              className="input"
              value={slug}
              onChange={(event) => {
                setSlugEdited(true);
                setSlug(event.target.value.toLowerCase());
              }}
              maxLength={maxSlugLength}
              autoCapitalize="none"
              spellCheck={false}
              aria-invalid={slugInvalid}
              aria-describedby="business-slug-status"
              required
            />
          </div>
          <p
            id="business-slug-status"
            className={slugInvalid ? 'field-error' : 'field-hint'}
            aria-live="polite"
          >
            {availability.state === 'checking' ? 'Checking...' : slugMessage}
          </p>
        </div>

        {formError && <ErrorMessage message={formError} />}

        <div>
          <button
            className="button"
            type="submit"
            disabled={submitting || !name.trim() || !slug || availability.state === 'unavailable'}
          >
            {submitting ? 'Starting...' : 'Start my business'}
          </button>
        </div>
      </form>
    </section>
  );
}
