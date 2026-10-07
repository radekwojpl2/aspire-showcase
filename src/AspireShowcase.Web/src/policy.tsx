import { useEffect, useState, type FormEvent } from 'react';
import { apiFetch, readProblem } from './api.ts';
import { describePolicy } from './policy-text.ts';
import { ErrorMessage } from './ui.tsx';

// User story V1-3: the owner stops clients cancelling or moving a booking at the last minute.
// New bookings get the policy; ones already made keep theirs.
export function CancellationPolicyCard() {
  const [noticeHours, setNoticeHours] = useState<number>();
  const [draft, setDraft] = useState('');
  const [error, setError] = useState<string>();
  const [status, setStatus] = useState('');
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    let current = true;
    apiFetch('/api/businesses/mine/cancellation-policy')
      .then(async (response) => {
        if (!current || !response.ok) return;
        const policy = (await response.json()) as { noticeHours: number };
        setNoticeHours(policy.noticeHours);
        setDraft(String(policy.noticeHours));
      })
      .catch((err) => current && setError(err instanceof Error ? err.message : 'Failed to call the API'));
    return () => {
      current = false;
    };
  }, []);

  if (noticeHours === undefined) return error ? <ErrorMessage message={error} /> : null;

  const save = async (event: FormEvent) => {
    event.preventDefault();
    setSaving(true);
    setError(undefined);
    setStatus('');
    try {
      const response = await apiFetch('/api/businesses/mine/cancellation-policy', {
        method: 'PUT',
        body: JSON.stringify({ noticeHours: draft === '' ? null : Number(draft) }),
      });
      if (response.ok) {
        const saved = (await response.json()) as { noticeHours: number };
        setNoticeHours(saved.noticeHours);
        setStatus('Saved. New bookings get this policy; ones already made keep theirs.');
      } else {
        const problem = await readProblem(response);
        setError(problem.errors?.noticeHours?.[0] ?? problem.title ?? `HTTP error! status: ${response.status}`);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to call the API');
    }
    setSaving(false);
  };

  return (
    <section className="card" aria-labelledby="policy-heading">
      <h2 id="policy-heading" className="section-title">Cancellation policy</h2>
      <form className="form" onSubmit={(event) => void save(event)} noValidate>
        <div className="field">
          <label htmlFor="policy-notice">Clients can cancel or move a booking up to, in hours before it</label>
          <input
            id="policy-notice"
            className="input input-narrow"
            type="number"
            min={0}
            max={168}
            step={1}
            value={draft}
            aria-invalid={Boolean(error)}
            aria-describedby="policy-hint"
            onChange={(event) => setDraft(event.target.value)}
          />
          <p id="policy-hint" className={error ? 'field-error' : 'field-hint'}>
            {error ?? `0 for until it starts. Clients now see: "${describePolicy(noticeHours)}" After that, they see your contact email.`}
          </p>
        </div>
        <div className="form-actions">
          <button className="button" type="submit" disabled={saving || draft === String(noticeHours)}>
            {saving ? 'Saving...' : 'Save policy'}
          </button>
        </div>
      </form>
      <p className="field-hint" role="status">{status}</p>
    </section>
  );
}
