// Every /api call goes through bff, which refuses it without this header. Other sites can't
// set it, so they can't make the browser call the API with the user's session cookie.
export function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const headers = new Headers(init.headers);
  headers.set('X-CSRF', '1');
  if (init.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json');
  return fetch(path, { ...init, headers });
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
