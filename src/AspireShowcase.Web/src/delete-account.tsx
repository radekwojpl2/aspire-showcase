import { useState, type FormEvent } from 'react';
import { apiFetch, readProblem } from './api.ts';
import { ErrorMessage } from './ui.tsx';

const confirmation = 'DELETE';

// User story V1-8: a client deletes their account and their data. Typing DELETE first makes it
// hard to do by accident; afterwards they're signed out, as the account no longer exists.
export function DeleteAccountCard({ isOwner }: { isOwner: boolean }) {
  const [typed, setTyped] = useState('');
  const [deleting, setDeleting] = useState(false);
  const [error, setError] = useState<string>();

  const remove = async (event: FormEvent) => {
    event.preventDefault();
    setDeleting(true);
    setError(undefined);
    try {
      const response = await apiFetch('/api/me', { method: 'DELETE' });
      if (!response.ok) throw new Error((await readProblem(response)).title ?? `HTTP error! status: ${response.status}`);
      signOut();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to call the API');
      setDeleting(false);
    }
  };

  return (
    <section className="card danger-zone" aria-labelledby="delete-account-heading">
      <h2 id="delete-account-heading" className="section-title">Delete my account</h2>
      {isOwner ? (
        <p className="hint">You own a business, so your account can't be deleted here yet.</p>
      ) : (
        <form className="form" onSubmit={(event) => void remove(event)}>
          <ul className="hint delete-account-effects">
            <li>Your upcoming bookings are cancelled, and each business is told.</li>
            <li>Past bookings stay with the businesses, without your name and email.</li>
            <li>Your account is deleted and you're signed out. This can't be undone.</li>
          </ul>
          {error && <ErrorMessage message={error} />}
          <div className="field">
            <label htmlFor="delete-account-confirm">Type {confirmation} to confirm</label>
            <input
              id="delete-account-confirm"
              className="input input-narrow"
              value={typed}
              onChange={(event) => setTyped(event.target.value)}
              autoComplete="off"
              spellCheck={false}
            />
          </div>
          <div className="form-actions">
            <button type="submit" className="button button-danger" disabled={typed !== confirmation || deleting}>
              {deleting ? 'Deleting...' : 'Delete my account'}
            </button>
          </div>
        </form>
      )}
    </section>
  );
}

// The same sign-out as the account menu's: a form post, so the browser follows bff's redirect to
// Logto, which ends its own session too. bff deletes its session, with the tokens in it.
function signOut() {
  const form = document.createElement('form');
  form.method = 'post';
  form.action = '/bff/logout';
  document.body.append(form);
  form.submit();
}
