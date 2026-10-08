import { useEffect, useState } from 'react';
import { apiFetch } from './api.ts';

// The signed-in owner's business, as GET /api/businesses/mine answers it.
export type MyBusiness = {
  id: string;
  name: string;
  slug: string;
  timeZone: string;
  contactEmail: string | null;
  createdAt: string;
  // The booking page's (V1-6); null until the owner sets them.
  address: string | null;
  description: string | null;
  logoUrl: string | null;
};

// The signed-in user's business: undefined while loading, null when they have none.
export function useMyBusiness(enabled: boolean) {
  const [business, setBusiness] = useState<MyBusiness | null>();
  const [error, setError] = useState<string>();

  useEffect(() => {
    if (!enabled) return;
    let current = true;
    apiFetch('/api/businesses/mine')
      .then(async (response) => {
        if (response.status === 404) return null;
        if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);
        return (await response.json()) as MyBusiness;
      })
      .then((result) => current && setBusiness(result))
      .catch((err) => current && setError(err instanceof Error ? err.message : 'Failed to call the API'));
    return () => {
      current = false;
    };
  }, [enabled]);

  return { business, error };
}
