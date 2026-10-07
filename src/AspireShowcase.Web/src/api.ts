import { signInUrl } from './session.ts';

// Every /api call goes through bff, which refuses it without this header. Other sites can't
// set it, so they can't make the browser call the API with the user's session cookie.
export async function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const headers = new Headers(init.headers);
  headers.set('X-CSRF', '1');
  if (init.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json');
  const response = await fetch(path, { ...init, headers });
  // bff ended the session (its refresh token was rejected): sign in again and come back here.
  // Only bff's own 401 does this; a 401 from the API itself redirecting could loop through Logto.
  if (response.status === 401 && response.headers.get('X-Session-Ended') === '1') {
    window.location.assign(signInUrl(window.location.pathname + window.location.search));
    // Never settles, so the page doesn't show an error while the browser navigates away.
    return new Promise<Response>(() => {});
  }
  return response;
}

// An error response from the API (RFC 9457 problem details), with field errors for validation.
export type Problem = {
  title?: string;
  errors?: Record<string, string[]>;
};

export async function readProblem(response: Response): Promise<Problem> {
  try {
    return (await response.json()) as Problem;
  } catch {
    return { title: `HTTP error! status: ${response.status}` };
  }
}
