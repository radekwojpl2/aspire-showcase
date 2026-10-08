import { useEffect, useState, type ChangeEvent, type FormEvent } from 'react';
import { apiFetch, readProblem } from './api.ts';
import { ErrorMessage } from './ui.tsx';

type Page = { slug: string; address: string | null; description: string | null; logoUrl: string | null };

// The API's limits, checked here too so the owner hears at once.
const maxAddress = 300;
const maxDescription = 1000;
const maxLogoBytes = 512 * 1024;
const logoTypes = ['image/png', 'image/jpeg', 'image/webp'];

// User story V1-6: what the booking page says about the business, so clients know they're in the
// right place: its address, a description and a logo.
export function BusinessPagePage() {
  const [page, setPage] = useState<Page>();
  const [address, setAddress] = useState('');
  const [description, setDescription] = useState('');
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [error, setError] = useState<string>();
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState<string>();
  const [logoBusy, setLogoBusy] = useState(false);

  useEffect(() => {
    let current = true;
    apiFetch('/api/businesses/mine')
      .then(async (response) => {
        if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);
        const found = (await response.json()) as Page;
        if (!current) return;
        setPage(found);
        setAddress(found.address ?? '');
        setDescription(found.description ?? '');
      })
      .catch((err) => current && setError(err instanceof Error ? err.message : 'Failed to call the API'));
    return () => {
      current = false;
    };
  }, []);

  if (!page) return error ? <ErrorMessage message={error} /> : <p className="status" role="status">Loading...</p>;

  // Each answer is the whole business again, with the logo's new address.
  const answer = async (response: Response, message: string) => {
    if (!response.ok) {
      const problem = await readProblem(response);
      if (problem.errors) setErrors(problem.errors);
      else setError(problem.title ?? `HTTP error! status: ${response.status}`);
      return;
    }
    setPage((await response.json()) as Page);
    setSaved(message);
  };

  const clear = () => {
    setErrors({});
    setError(undefined);
    setSaved(undefined);
  };

  const save = async (event: FormEvent) => {
    event.preventDefault();
    setSaving(true);
    clear();
    try {
      await answer(
        await apiFetch('/api/businesses/mine/page', { method: 'PUT', body: JSON.stringify({ address, description }) }),
        'Saved. Your booking page shows it now.',
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to call the API');
    }
    setSaving(false);
  };

  const upload = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (!file) return;
    clear();
    if (!logoTypes.includes(file.type)) return setErrors({ logo: ['Use a PNG, JPEG or WebP image.'] });
    if (file.size > maxLogoBytes) return setErrors({ logo: [`Use an image of at most ${maxLogoBytes / 1024} KB.`] });
    setLogoBusy(true);
    try {
      // The file itself is the body, with its own type.
      await answer(
        await apiFetch('/api/businesses/mine/logo', { method: 'PUT', body: file, headers: { 'Content-Type': file.type } }),
        'Logo saved.',
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to call the API');
    }
    setLogoBusy(false);
  };

  const removeLogo = async () => {
    clear();
    setLogoBusy(true);
    try {
      await answer(await apiFetch('/api/businesses/mine/logo', { method: 'DELETE' }), 'Logo removed.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to call the API');
    }
    setLogoBusy(false);
  };

  return (
    <section className="card" aria-labelledby="business-page-heading">
      <h2 id="business-page-heading" className="section-title">Business page</h2>
      <p className="hint">
        What clients see at the top of <a href={`/book/${encodeURIComponent(page.slug)}`}>your booking page</a>.
      </p>
      {error && <ErrorMessage message={error} />}
      <p className="field-hint" role="status">{saved ?? ''}</p>

      <div className="field">
        <span className="field-label">Logo</span>
        <div className="logo-editor">
          {page.logoUrl ? (
            <img className="business-logo" src={page.logoUrl} alt="Your logo" />
          ) : (
            <p className="hint">No logo yet.</p>
          )}
          <label className="button button-secondary">
            {page.logoUrl ? 'Change logo' : 'Add a logo'}
            <input
              type="file"
              className="visually-hidden"
              accept={logoTypes.join(',')}
              disabled={logoBusy}
              aria-describedby="business-logo-hint"
              onChange={(event) => void upload(event)}
            />
          </label>
          {page.logoUrl && (
            <button type="button" className="button button-secondary" disabled={logoBusy} onClick={() => void removeLogo()}>
              Remove
            </button>
          )}
        </div>
        <p id="business-logo-hint" className={errors.logo ? 'field-error' : 'field-hint hint'}>
          {errors.logo?.[0] ?? `PNG, JPEG or WebP, up to ${maxLogoBytes / 1024} KB.`}
        </p>
      </div>

      <form className="form" onSubmit={(event) => void save(event)} noValidate>
        <div className="field">
          <label htmlFor="business-address">Address</label>
          <textarea
            id="business-address"
            className="input"
            rows={3}
            maxLength={maxAddress}
            value={address}
            onChange={(event) => setAddress(event.target.value)}
            aria-invalid={Boolean(errors.address)}
            aria-describedby="business-address-hint"
          />
          <p id="business-address-hint" className={errors.address ? 'field-error' : 'field-hint hint'}>
            {errors.address?.[0] ?? 'Where clients come to, on as many lines as you like.'}
          </p>
        </div>
        <div className="field">
          <label htmlFor="business-description">Description</label>
          <textarea
            id="business-description"
            className="input"
            rows={5}
            maxLength={maxDescription}
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            aria-invalid={Boolean(errors.description)}
            aria-describedby="business-description-hint"
          />
          <p id="business-description-hint" className={errors.description ? 'field-error' : 'field-hint hint'}>
            {errors.description?.[0] ?? `What you do, in up to ${maxDescription} characters.`}
          </p>
        </div>
        <div className="form-actions">
          <button
            type="submit"
            className="button"
            disabled={saving || (address === (page.address ?? '') && description === (page.description ?? ''))}
          >
            {saving ? 'Saving...' : 'Save'}
          </button>
        </div>
      </form>
    </section>
  );
}
