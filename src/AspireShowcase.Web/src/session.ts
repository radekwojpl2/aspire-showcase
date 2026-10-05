import { createContext, useContext } from 'react';

export type SessionUser = {
  name: string;
  email: string | null;
  // The access token has the owner role's permission.
  isOwner: boolean;
};

export type Session = {
  signInEnabled: boolean;
  user: SessionUser | null;
};

// Who is signed in, as bff sees it. The app never gets a token: bff keeps the tokens and the
// session cookie is HttpOnly, so asking bff is the only way to know about the user.
export function loadSession(): Promise<Session> {
  return fetch('/bff/user')
    .then((response) => (response.ok ? response.json() : Promise.reject()))
    .catch(() => ({ signInEnabled: false, user: null }));
}

export const SessionContext = createContext<Session>({ signInEnabled: false, user: null });

export function useSession(): Session {
  return useContext(SessionContext);
}

// Sign-in is a page navigation through bff to Logto and back to returnUrl. With signUp,
// Logto opens on its "create account" form.
export function signInUrl(returnUrl: string, signUp = false): string {
  const query = new URLSearchParams({ returnUrl });
  if (signUp) query.set('signup', 'true');
  return `/bff/login?${query}`;
}
